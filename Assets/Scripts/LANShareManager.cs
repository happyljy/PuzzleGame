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
    #region 单例

    public static LANShareManager Instance { get; private set; }

    #endregion

    #region 常量

    private const int DiscoveryPort = 8888;          // UDP 广播端口
    private const int FileTransferPort = 5555;       // TCP 文件传输端口
    private const float ConnectTimeoutSeconds = 3f;  // 连接超时时间

    #endregion

    #region 对外属性与事件

    /// <summary>服务端：是否有客户端已连接。</summary>
    public bool ClientConnected => clientConnected;

    /// <summary>客户端：是否已连接到服务端。</summary>
    public bool ConnectedToServer => connectedClient != null && connectedClient.Connected;

    /// <summary>客户端：最近一次连接的服务器 IP。</summary>
    public string LastConnectedServerIP { get; set; }

    /// <summary>分享停止事件（服务端/客户端均可订阅）。</summary>
    public event Action OnSharingStopped;

    #endregion

    #region 私有字段

    // UDP 客户端（服务端广播 / 客户端监听）
    private UdpClient broadcastUdpClient;
    private UdpClient discoveryUdpClient;

    // 状态标记
    private bool isDiscovering = false;
    private bool isSharing = false;
    private bool isServerRunning = false;

    // TCP 服务器与客户端
    private TcpListener tcpListener;
    private TcpClient connectedClient;

    // 分享数据
    private List<string> sharedFiles = new List<string>();
    private string deviceName;

    // 客户端连接标记（volatile 保证跨线程可见性）
    private volatile bool clientConnected = false;

    // 自动停止分享协程
    private Coroutine autoStopCoroutine;

    #endregion

    #region Unity 生命周期

    private void Awake()
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

    private void OnDestroy()
    {
        StopSharing();
        StopDiscovery();
        DisconnectFromServer();
        StopFileServer();
    }

    #endregion

    #region 分享方（服务器）

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

    /// <summary>
    /// 广播常规发现消息（未就绪）。
    /// </summary>
    private void BroadcastPresence()
    {
        if (broadcastUdpClient == null) return;

        string message = $"PUZZLE_SHARE|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        // 同时发送到子网广播和受限广播，提高被发现概率
        SendBroadcast(data, "子网广播");
        SendBroadcast(data, "受限广播", true);

        Debug.Log($"广播: {message}");
    }

    /// <summary>
    /// 发送就绪广播（客户端收到后可以识别设备已就绪，但连接仍由客户端手动触发）。
    /// </summary>
    public void NotifyClientsReady()
    {
        if (!isSharing || broadcastUdpClient == null) return;

        string message = $"PUZZLE_SHARE_READY|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        SendBroadcast(data, "就绪广播(子网)");
        SendBroadcast(data, "就绪广播(受限)", true);

        Debug.Log($"发送就绪广播: {message}");
    }

    /// <summary>
    /// 发送广播数据到子网广播或受限广播地址。
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="logTag">日志标签</param>
    /// <param name="useLimitedBroadcast">是否使用受限广播地址（255.255.255.255）</param>
    private void SendBroadcast(byte[] data, string logTag, bool useLimitedBroadcast = false)
    {
        try
        {
            IPEndPoint ep = useLimitedBroadcast
                ? new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)
                : new IPEndPoint(IPAddress.Parse(GetBroadcastAddress()), DiscoveryPort);
            broadcastUdpClient.Send(data, data.Length, ep);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{logTag}发送失败: {e.Message}");
        }
    }

    /// <summary>
    /// 停止广播（只停止广播，不停止 TCP 服务器）。
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
    /// 停止分享（停止广播和 TCP 服务器）。
    /// </summary>
    public void StopSharing()
    {
        isSharing = false;
        StopBroadcast();
        StopFileServer();
        clientConnected = false;
        OnSharingStopped?.Invoke();
    }

    /// <summary>
    /// 60 秒内没有客户端连接则自动停止分享。
    /// </summary>
    private IEnumerator AutoStopSharingAfterTimeout(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (!clientConnected)
        {
            Debug.Log("分享超时，自动停止");
            StopSharing();
        }
    }

    /// <summary>
    /// 设置要分享的文件列表（由 UI 调用）。
    /// </summary>
    public void SetSharedFiles(List<string> files)
    {
        sharedFiles = files;
    }

    #endregion

    #region 发现方（客户端）

    /// <summary>
    /// 开始发现设备。
    /// </summary>
    /// <param name="onDeviceFound">回调参数：设备名, IP, 端口, 是否就绪广播</param>
    public void StartDiscovery(Action<string, string, int, bool> onDeviceFound)
    {
        if (isDiscovering) return;
        isDiscovering = true;

        discoveryUdpClient = new UdpClient(DiscoveryPort);
        discoveryUdpClient.BeginReceive(OnDiscoveryReceive, new object[] { discoveryUdpClient, onDeviceFound });
        Debug.Log($"开始发现设备，监听端口 {DiscoveryPort}");
    }

    /// <summary>
    /// 收到 UDP 广播后的回调。
    /// </summary>
    private void OnDiscoveryReceive(IAsyncResult ar)
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
                callback?.Invoke(parts[1], parts[2], int.Parse(parts[3]), true);
        }
        else if (message.StartsWith("PUZZLE_SHARE|"))
        {
            string[] parts = message.Split('|');
            if (parts.Length == 4)
                callback?.Invoke(parts[1], parts[2], int.Parse(parts[3]), false);
        }

        try { client.BeginReceive(OnDiscoveryReceive, ar); } catch { }
    }

    /// <summary>
    /// 停止发现设备。
    /// </summary>
    public void StopDiscovery()
    {
        isDiscovering = false;
        if (discoveryUdpClient != null)
        {
            discoveryUdpClient.Close();
            discoveryUdpClient = null;
        }
    }

    #endregion

    #region TCP 服务器

    /// <summary>
    /// 启动 TCP 服务器，开始接受客户端连接。
    /// </summary>
    private void StartFileServer()
    {
        tcpListener = new TcpListener(IPAddress.Any, FileTransferPort);
        tcpListener.Start();
        isServerRunning = true;
        tcpListener.BeginAcceptTcpClient(OnClientConnected, null);
    }

    /// <summary>
    /// 客户端连接成功后的回调。
    /// </summary>
    private void OnClientConnected(IAsyncResult ar)
    {
        if (!isServerRunning) return;

        TcpClient client = tcpListener.EndAcceptTcpClient(ar);
        clientConnected = true;

        // 有客户端连接后取消自动停止
        if (autoStopCoroutine != null)
        {
            StopCoroutine(autoStopCoroutine);
            autoStopCoroutine = null;
        }

        // 在后台线程处理该客户端的请求
        List<string> filesToShare = sharedFiles.Count > 0 ? sharedFiles : GameDataManager.GetUploadedImages();
        System.Threading.Tasks.Task.Run(() => HandleClient(client, filesToShare));

        // 继续接受下一个客户端
        tcpListener.BeginAcceptTcpClient(OnClientConnected, null);
    }

    /// <summary>
    /// 处理客户端请求（LIST 获取列表 / GET 下载文件）。
    /// </summary>
    private void HandleClient(TcpClient client, List<string> files)
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
                    HandleFileRequest(stream, request.Substring(4));
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

    /// <summary>
    /// 处理单个文件下载请求。
    /// </summary>
    private void HandleFileRequest(NetworkStream stream, string fileName)
    {
        string filePath = Path.Combine(Application.persistentDataPath, "Uploads", fileName);

        if (!File.Exists(filePath))
        {
            stream.Write(BitConverter.GetBytes(0), 0, 4);
            stream.Flush();
            Debug.LogWarning($"请求的文件不存在: {filePath}");
            return;
        }

        byte[] fileData = File.ReadAllBytes(filePath);
        byte[] lengthBytes = BitConverter.GetBytes(fileData.Length);
        stream.Write(lengthBytes, 0, 4);
        stream.Write(fileData, 0, fileData.Length);
        stream.Flush();

        Debug.Log($"发送图片: {fileName} ({fileData.Length} bytes)");
    }

    /// <summary>
    /// 停止 TCP 服务器。
    /// </summary>
    private void StopFileServer()
    {
        isServerRunning = false;
        if (tcpListener != null)
        {
            tcpListener.Stop();
            tcpListener = null;
        }
    }

    #endregion

    #region TCP 客户端

    /// <summary>
    /// 连接到服务端（带 3 秒超时）。
    /// </summary>
    public void ConnectToServer(string serverIP, int port)
    {
        if (connectedClient != null && connectedClient.Connected) return;

        TcpClient client = new TcpClient();
        try
        {
            IAsyncResult result = client.BeginConnect(serverIP, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(3000); // 最多等待 3 秒

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

    /// <summary>
    /// 断开与服务端的连接。
    /// </summary>
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

    /// <summary>
    /// 请求服务端的图片列表。
    /// </summary>
    public void DownloadImageList(Action<List<string>> onListReceived)
    {
        if (!ConnectedToServer)
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

    /// <summary>
    /// 下载单个图片（保存到 Uploads 文件夹）。
    /// </summary>
    public void DownloadImage(string fileName, Action<string> onDownloaded)
    {
        DownloadImageInternal(fileName, false, onDownloaded);
    }

    /// <summary>
    /// 下载单个图片（保存到 Shared 文件夹）。
    /// </summary>
    public void DownloadImageToShared(string fileName, Action<string> onDownloaded)
    {
        DownloadImageInternal(fileName, true, onDownloaded);
    }

    /// <summary>
    /// 下载图片的内部实现，根据 saveToShared 决定保存目录。
    /// </summary>
    private void DownloadImageInternal(string fileName, bool saveToShared, Action<string> onDownloaded)
    {
        if (!ConnectedToServer)
        {
            Debug.LogError("未连接到服务器");
            return;
        }

        try
        {
            NetworkStream stream = connectedClient.GetStream();
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine($"GET|{fileName}");

            // 读取文件长度（前 4 字节）
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

            // 保存文件
            string targetDir = saveToShared
                ? Path.Combine(Application.persistentDataPath, "Shared")
                : Path.Combine(Application.persistentDataPath, "Uploads");
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

            // 记录到数据管理器
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

    #endregion

    #region 工具方法

    /// <summary>
    /// 获取本机局域网 IP（过滤回环、自动配置、虚拟网卡地址）。
    /// </summary>
    private string GetLocalIPAddress()
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

    /// <summary>
    /// 根据本机 IP 计算子网广播地址（例如 192.168.1.255）。
    /// </summary>
    private string GetBroadcastAddress()
    {
        string localIP = GetLocalIPAddress();
        if (string.IsNullOrEmpty(localIP)) return "255.255.255.255";

        string[] parts = localIP.Split('.');
        return parts[0] + "." + parts[1] + "." + parts[2] + ".255";
    }

    #endregion

    #region 辅助数据结构

    /// <summary>
    /// 用于 JSON 序列化/反序列化的字符串列表包装类。
    /// </summary>
    [Serializable]
    public class StringListWrapper
    {
        public List<string> items;

        public StringListWrapper() { }
        public StringListWrapper(List<string> items) { this.items = items; }
    }

    #endregion
}