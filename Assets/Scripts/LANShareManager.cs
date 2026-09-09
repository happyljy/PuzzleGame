using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// 局域网分享管理器：负责设备发现、TCP 连接、文件传输。
/// 服务端（分享方）：启动分享 -> 等待客户端连接 -> 选择要分享的图片 -> 发送就绪广播 -> 响应客户端下载请求。
/// 客户端（接收方）：搜索设备 -> 连接设备 -> 手动选择下载图片到共享分类。
/// </summary>
public class LANShareManager : MonoBehaviour
{
    public static LANShareManager Instance { get; private set; }

    private const int DiscoveryPort = 8888;          // UDP 广播端口
    private const int FileTransferPort = 5555;       // TCP 文件传输端口

    private UdpClient broadcastUdpClient;             // 服务端广播专用
    private UdpClient discoveryUdpClient;             // 客户端监听专用

    private bool isDiscovering = false;
    private bool isSharing = false;
    private TcpListener tcpListener;
    private bool isServerRunning = false;
    private TcpClient connectedClient;                // 客户端主动连接的 TcpClient
    private List<string> sharedFiles = new List<string>(); // 服务端选择要分享的文件名列表
    private string deviceName;
    private volatile bool clientConnected = false;    // 服务端：是否有客户端连接
    public bool ClientConnected => clientConnected;
    public bool ConnectedToServer => connectedClient != null && connectedClient.Connected;
    public string LastConnectedServerIP { get; set; }
    public event Action OnSharingStopped;

    private Coroutine autoStopCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        deviceName = SystemInfo.deviceName;
    }

    void OnDestroy()
    {
        StopSharing();
        StopDiscovery();
        DisconnectFromServer();
        StopFileServer();
    }

    // ==================== 分享方（服务器） ====================

    /// <summary>
    /// 启动分享：开始 TCP 服务器并广播常规消息，等待客户端连接。
    /// </summary>
    public void StartSharing()
    {
        if (isSharing) return;
        isSharing = true;
        StartFileServer();

        broadcastUdpClient = new UdpClient();
        broadcastUdpClient.EnableBroadcast = true;
        InvokeRepeating(nameof(BroadcastPresence), 0f, 2f);
        clientConnected = false;

        if (autoStopCoroutine != null) StopCoroutine(autoStopCoroutine);
        autoStopCoroutine = StartCoroutine(AutoStopSharingAfterTimeout(60f));

        Debug.Log($"开始分享，本机IP: {GetLocalIPAddress()}");
        Debug.Log($"广播地址: {GetBroadcastAddress()}");
    }

    void BroadcastPresence()
    {
        if (broadcastUdpClient == null) return;
        string message = $"PUZZLE_SHARE|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        // 同时发送到子网广播和受限广播，提高被发现概率
        try
        {
            IPEndPoint subnetBroadcast = new IPEndPoint(IPAddress.Parse(GetBroadcastAddress()), DiscoveryPort);
            broadcastUdpClient.Send(data, data.Length, subnetBroadcast);
        }
        catch (Exception e) { Debug.LogWarning($"子网广播发送失败: {e.Message}"); }

        try
        {
            IPEndPoint limitedBroadcast = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            broadcastUdpClient.Send(data, data.Length, limitedBroadcast);
        }
        catch (Exception e) { Debug.LogWarning($"受限广播发送失败: {e.Message}"); }

        Debug.Log($"广播: {message}");
    }

    /// <summary>
    /// 发送就绪广播（客户端收到后可以识别设备已就绪，但连接仍由客户端手动触发）
    /// </summary>
    public void NotifyClientsReady()
    {
        if (!isSharing || broadcastUdpClient == null) return;
        string message = $"PUZZLE_SHARE_READY|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        try
        {
            IPEndPoint subnetBroadcast = new IPEndPoint(IPAddress.Parse(GetBroadcastAddress()), DiscoveryPort);
            broadcastUdpClient.Send(data, data.Length, subnetBroadcast);
        }
        catch (Exception e) { Debug.LogWarning($"就绪广播(子网)发送失败: {e.Message}"); }

        try
        {
            IPEndPoint limitedBroadcast = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            broadcastUdpClient.Send(data, data.Length, limitedBroadcast);
        }
        catch (Exception e) { Debug.LogWarning($"就绪广播(受限)发送失败: {e.Message}"); }

        Debug.Log($"发送就绪广播: {message}");
    }

    /// <summary>
    /// 停止广播（只停止广播，不停止TCP服务器）
    /// </summary>
    public void StopBroadcast()
    {
        CancelInvoke(nameof(BroadcastPresence));
        if (broadcastUdpClient != null)
        {
            broadcastUdpClient.Close();
            broadcastUdpClient = null;
        }
        if (autoStopCoroutine != null)
        {
            StopCoroutine(autoStopCoroutine);
            autoStopCoroutine = null;
        }
        Debug.Log("广播已停止");
    }

    /// <summary>
    /// 停止分享（停止广播和TCP服务器）
    /// </summary>
    public void StopSharing()
    {
        isSharing = false;
        StopBroadcast();
        StopFileServer();
        clientConnected = false;
        OnSharingStopped?.Invoke();
    }

    IEnumerator AutoStopSharingAfterTimeout(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (!clientConnected)
        {
            Debug.Log("分享超时，自动停止");
            StopSharing();
        }
    }

    public void SetSharedFiles(List<string> files)
    {
        sharedFiles = files;
    }

    // ==================== 发现方（客户端） ====================

    public void StartDiscovery(Action<string, string, int, bool> onDeviceFound)
    {
        if (isDiscovering) return;
        isDiscovering = true;

        discoveryUdpClient = new UdpClient(DiscoveryPort);
        discoveryUdpClient.BeginReceive(OnDiscoveryReceive, new object[] { discoveryUdpClient, onDeviceFound });
        Debug.Log($"开始发现设备，监听端口 {DiscoveryPort}");
    }

    void OnDiscoveryReceive(IAsyncResult ar)
    {
        object[] args = (object[])ar.AsyncState;
        UdpClient client = (UdpClient)args[0];
        Action<string, string, int, bool> callback = (Action<string, string, int, bool>)args[1];

        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        byte[] data = client.EndReceive(ar, ref remoteEP);
        string message = Encoding.UTF8.GetString(data);
        Debug.Log($"收到数据: '{message}' 来自 {remoteEP}");

        if (message.StartsWith("PUZZLE_SHARE_READY|"))
        {
            string[] parts = message.Split('|');
            if (parts.Length == 4)
            {
                callback?.Invoke(parts[1], parts[2], int.Parse(parts[3]), true);
            }
        }
        else if (message.StartsWith("PUZZLE_SHARE|"))
        {
            string[] parts = message.Split('|');
            if (parts.Length == 4)
            {
                callback?.Invoke(parts[1], parts[2], int.Parse(parts[3]), false);
            }
        }

        try { client.BeginReceive(OnDiscoveryReceive, ar); } catch { }
    }

    public void StopDiscovery()
    {
        isDiscovering = false;
        if (discoveryUdpClient != null)
        {
            discoveryUdpClient.Close();
            discoveryUdpClient = null;
        }
    }

    // ==================== TCP 服务器 ====================

    void StartFileServer()
    {
        tcpListener = new TcpListener(IPAddress.Any, FileTransferPort);
        tcpListener.Start();
        isServerRunning = true;
        tcpListener.BeginAcceptTcpClient(OnClientConnected, null);
    }

    void OnClientConnected(IAsyncResult ar)
    {
        if (!isServerRunning) return;
        TcpClient client = tcpListener.EndAcceptTcpClient(ar);

        clientConnected = true;

        if (autoStopCoroutine != null)
        {
            StopCoroutine(autoStopCoroutine);
            autoStopCoroutine = null;
        }

        List<string> filesToShare = sharedFiles.Count > 0 ? sharedFiles : GameDataManager.GetUploadedImages();
        System.Threading.Tasks.Task.Run(() => HandleClient(client, filesToShare));

        tcpListener.BeginAcceptTcpClient(OnClientConnected, null);
    }

    void HandleClient(TcpClient client, List<string> files)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            while (client.Connected)
            {
                string request = reader.ReadLine();
                if (request == null) break;

                if (request == "LIST")
                {
                    List<string> filesToSend = sharedFiles.Count > 0 ? sharedFiles : files;
                    string listJson = JsonUtility.ToJson(new StringListWrapper(filesToSend));
                    writer.WriteLine(listJson);
                    Debug.Log($"发送文件列表，共 {filesToSend.Count} 张图片");
                }
                else if (request.StartsWith("GET|"))
                {
                    string fileName = request.Substring(4);
                    string filePath = Path.Combine(Application.persistentDataPath, "Uploads", fileName);

                    if (!File.Exists(filePath))
                    {
                        stream.Write(BitConverter.GetBytes(0), 0, 4);
                        stream.Flush();
                        Debug.LogWarning($"请求的文件不存在: {filePath}");
                        continue;
                    }

                    byte[] fileData = File.ReadAllBytes(filePath);
                    byte[] lengthBytes = BitConverter.GetBytes(fileData.Length);
                    stream.Write(lengthBytes, 0, 4);
                    stream.Write(fileData, 0, fileData.Length);
                    stream.Flush();

                    Debug.Log($"发送图片: {fileName} ({fileData.Length} bytes)");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"处理客户端请求出错: {e.Message}");
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    void StopFileServer()
    {
        isServerRunning = false;
        if (tcpListener != null)
        {
            tcpListener.Stop();
            tcpListener = null;
        }
    }

    // ==================== TCP 客户端 ====================

    public void ConnectToServer(string serverIP, int port)
    {
        if (connectedClient != null && connectedClient.Connected) return;

        TcpClient client = new TcpClient();
        try
        {
            IAsyncResult result = client.BeginConnect(serverIP, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(3000); // 最多等待3秒
            if (!success)
            {
                client.Close();
                Debug.LogError($"连接超时: {serverIP}:{port}");
                return;
            }
            client.EndConnect(result);
            connectedClient = client;
            LastConnectedServerIP = serverIP;
            Debug.Log($"已连接到 {serverIP}:{port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"连接失败: {e.Message}");
            client.Close();
        }
    }

    public void DisconnectFromServer()
    {
        if (connectedClient != null)
        {
            connectedClient.Close();
            connectedClient = null;
            LastConnectedServerIP = null;
            Debug.Log("已断开连接");
        }
    }

    public void DownloadImageList(Action<List<string>> onListReceived)
    {
        if (connectedClient == null || !connectedClient.Connected)
        {
            Debug.LogError("未连接到服务器");
            return;
        }

        try
        {
            NetworkStream stream = connectedClient.GetStream();
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine("LIST");

            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            string json = reader.ReadLine();

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("服务器返回的图片列表为空");
                onListReceived?.Invoke(new List<string>());
                return;
            }

            StringListWrapper wrapper = JsonUtility.FromJson<StringListWrapper>(json);
            onListReceived?.Invoke(wrapper?.items ?? new List<string>());
        }
        catch (Exception e)
        {
            Debug.LogError($"获取远程图片列表失败: {e.Message}");
            onListReceived?.Invoke(new List<string>());
        }
    }

    public void DownloadImage(string fileName, Action<string> onDownloaded)
    {
        DownloadImageInternal(fileName, false, onDownloaded);
    }

    public void DownloadImageToShared(string fileName, Action<string> onDownloaded)
    {
        DownloadImageInternal(fileName, true, onDownloaded);
    }

    private void DownloadImageInternal(string fileName, bool saveToShared, Action<string> onDownloaded)
    {
        if (connectedClient == null || !connectedClient.Connected)
        {
            Debug.LogError("未连接到服务器");
            return;
        }

        try
        {
            NetworkStream stream = connectedClient.GetStream();
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine($"GET|{fileName}");

            byte[] lengthBytes = new byte[4];
            int bytesRead = 0;
            while (bytesRead < 4)
            {
                int read = stream.Read(lengthBytes, bytesRead, 4 - bytesRead);
                if (read <= 0)
                    throw new IOException("服务器在发送文件长度前关闭了连接");
                bytesRead += read;
            }

            int fileLength = BitConverter.ToInt32(lengthBytes, 0);
            if (fileLength <= 0)
            {
                Debug.LogWarning($"服务器没有返回有效文件: {fileName}");
                return;
            }

            string targetDir = saveToShared ? Path.Combine(Application.persistentDataPath, "Shared") : Path.Combine(Application.persistentDataPath, "Uploads");
            Directory.CreateDirectory(targetDir);
            string destPath = Path.Combine(targetDir, fileName);

            using (FileStream fs = File.Create(destPath))
            {
                byte[] buffer = new byte[81920];
                int totalReceived = 0;
                while (totalReceived < fileLength)
                {
                    int read = stream.Read(buffer, 0, Math.Min(buffer.Length, fileLength - totalReceived));
                    if (read <= 0)
                        throw new IOException($"图片 {fileName} 下载中断，已收到 {totalReceived}/{fileLength} bytes");

                    fs.Write(buffer, 0, read);
                    totalReceived += read;
                }
            }

            if (saveToShared)
                GameDataManager.AddSharedImage(fileName);
            else
                GameDataManager.AddUploadedImage(fileName);

            Debug.Log($"图片下载完成: {fileName}");
            onDownloaded?.Invoke(destPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"下载图片 {fileName} 失败: {e.Message}");
        }
    }

    // ==================== 工具 ====================

    string GetLocalIPAddress()
    {
        string localIP = "";
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(ip) &&
                !ip.ToString().StartsWith("169.254") &&   // 排除自动配置
                !ip.ToString().StartsWith("192.168.56") && // 排除 VirtualBox
                !ip.ToString().StartsWith("192.168.99"))   // 排除 Docker
            {
                localIP = ip.ToString();
                break;
            }
        }
        return localIP;
    }

    string GetBroadcastAddress()
    {
        string localIP = GetLocalIPAddress();
        if (string.IsNullOrEmpty(localIP)) return "255.255.255.255";

        string[] parts = localIP.Split('.');
        return parts[0] + "." + parts[1] + "." + parts[2] + ".255";
    }

    [Serializable]
    public class StringListWrapper
    {
        public List<string> items;
        public StringListWrapper() { }
        public StringListWrapper(List<string> items) { this.items = items; }
    }
}