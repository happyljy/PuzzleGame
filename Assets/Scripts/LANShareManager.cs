using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// 局域网分享管理器。
///
/// 通信协议：所有消息统一为 [1字节类型][4字节长度][payload]
///   类型 0x01 = 文本（UTF-8）
///   类型 0x02 = 文件（二进制）
///
/// 线程安全：
///   - Log() 只入队，不调用任何 Unity API
///   - Update() 在主线程出队，写 Debug.Log + UI
///   - UDP/TCP 回调通过 EnqueueMainThread 调度
///   - Unity API（persistentDataPath 等）在 Awake 缓存，供后台线程使用
/// </summary>
public class LANShareManager : MonoBehaviour
{
    #region 单例

    public static LANShareManager Instance { get; private set; }

    #endregion

    #region 常量

    private const int DiscoveryPort = 8888;
    private const int FileTransferPort = 5555;
    private const int ConnectTimeoutMs = 3000;
    private const float AutoStopSharingTimeout = 600f;
    private const float ReadyBroadcastDuration = 120f;
    private const float ReadyBroadcastInterval = 1.5f;

    private const byte MSG_TEXT = 0x01;
    private const byte MSG_FILE = 0x02;
    private const int MaxPayloadSize = 16 * 1024 * 1024;

    #endregion

    #region 对外属性与事件

    public bool ClientConnected => clientConnected;
    public bool ConnectedToServer => connectedClient != null && clientStream != null;
    public string LastConnectedServerIP { get; set; }
    public event Action OnSharingStopped;

    #endregion

    #region 私有字段

    // ★ 缓存 Unity API（后台线程不能访问）
    private string persistentDataPath;
    private string uploadsDirectory;
    private string sharedDirectory;

    private UdpClient broadcastUdpClient;
    private UdpClient discoveryUdpClient;

    private bool isDiscovering = false;
    private bool isSharing = false;
    private bool isServerRunning = false;

    private TcpListener tcpListener;
    private TcpClient connectedClient;
    private NetworkStream clientStream;

    private List<string> sharedFiles = new List<string>();
    private string deviceName;

    private volatile bool clientConnected = false;

    private Coroutine autoStopCoroutine;
    private Coroutine readyBroadcastCoroutine;

    // 调试
    private readonly Queue<string> logQueue = new Queue<string>();
    private readonly object logQueueLock = new object();
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object mainThreadActionsLock = new object();
    private string lastLoggedBroadcastIP = "";

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        deviceName = SystemInfo.deviceName;

        // ★ 在主线程缓存 Unity API，供后台线程使用
        persistentDataPath = Application.persistentDataPath;
        uploadsDirectory = Path.Combine(persistentDataPath, "Uploads");
        sharedDirectory = Path.Combine(persistentDataPath, "Shared");

        Log($"LANShareManager 初始化，deviceName={deviceName}");
        Log($"persistentDataPath = {persistentDataPath}");
    }

    private void OnDestroy()
    {
        StopSharing();
        StopDiscovery();
        DisconnectFromServer();
        StopFileServer();
    }

    private void Update()
    {
        // 1. 处理后台线程调度过来的主线程任务
        while (true)
        {
            Action action = null;
            lock (mainThreadActionsLock)
            {
                if (mainThreadActions.Count == 0) break;
                action = mainThreadActions.Dequeue();
            }
            try { action?.Invoke(); }
            catch (Exception e) { Debug.LogError($"[LAN] 主线程任务异常: {e}"); }
        }

        // 2. 日志出队，写 UI（主线程）
        while (true)
        {
            string line = null;
            lock (logQueueLock)
            {
                if (logQueue.Count == 0) break;
                line = logQueue.Dequeue();
            }
            if (MainMenuManager.Instance != null)
                MainMenuManager.Instance.UploadDebug(line);
            else
                Debug.Log(line);
        }
    }

    #endregion

    #region 协议实现

    private static void SendText(NetworkStream stream, string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] header = new byte[5];
        header[0] = MSG_TEXT;
        Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 1, 4);
        stream.Write(header, 0, 5);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private static void SendFile(NetworkStream stream, byte[] data)
    {
        if (data == null) data = new byte[0];
        byte[] header = new byte[5];
        header[0] = MSG_FILE;
        Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, header, 1, 4);
        stream.Write(header, 0, 5);
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    /// <summary>
    /// 读一条消息。失败时通过 reason 返回原因。
    /// </summary>
    private static bool ReceiveMessage(NetworkStream stream, out byte type, out byte[] payload, out string reason)
    {
        type = 0;
        payload = null;
        reason = null;

        byte[] header = new byte[5];
        int headerRead = ReadExact(stream, header, 5);
        if (headerRead != 5)
        {
            reason = $"header 读取失败 ({headerRead}/5 字节)，对端可能已关闭";
            return false;
        }

        type = header[0];
        int len = BitConverter.ToInt32(header, 1);
        if (len < 0 || len > MaxPayloadSize)
        {
            reason = $"非法长度 {len}";
            return false;
        }

        payload = new byte[len];
        int payloadRead = ReadExact(stream, payload, len);
        if (payloadRead != len)
        {
            reason = $"payload 读取失败 ({payloadRead}/{len} 字节)";
            return false;
        }
        return true;
    }

    private static int ReadExact(NetworkStream stream, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = stream.Read(buf, total, count - total); }
            catch { return total; }
            if (read <= 0) return total;
            total += read;
        }
        return total;
    }

    #endregion

    #region 调试辅助（线程安全）

    private void Log(string msg)
    {
        string line = $"[LAN] {msg}";
        lock (logQueueLock)
        {
            if (logQueue.Count > 800) logQueue.Dequeue();
            logQueue.Enqueue(line);
        }
    }

    private void EnqueueMainThread(Action action)
    {
        if (action == null) return;
        lock (mainThreadActionsLock) mainThreadActions.Enqueue(action);
    }

    #endregion

    #region 分享方（服务器）

    public void StartSharing()
    {
        if (isSharing) { Log("已经在分享中，忽略"); return; }
        isSharing = true;

        Log("========== 开始分享 ==========");
        Log($"本机IP: {GetLocalIPAddress()}");
        Log($"广播地址: {GetBroadcastAddress()}");
        Log($"设备名: {deviceName}");

        StartFileServer();

        try
        {
            broadcastUdpClient = new UdpClient();
            broadcastUdpClient.EnableBroadcast = true;
        }
        catch (Exception e)
        {
            Log($"[错误] 创建广播 UdpClient 失败: {e.Message}");
            return;
        }

        InvokeRepeating(nameof(BroadcastPresence), 0f, 2f);
        clientConnected = false;
        lastLoggedBroadcastIP = "";

        if (autoStopCoroutine != null) StopCoroutine(autoStopCoroutine);
        autoStopCoroutine = StartCoroutine(AutoStopSharingAfterTimeout(AutoStopSharingTimeout));

        Log("分享已启动，等待客户端连接");
    }

    private void BroadcastPresence()
    {
        if (broadcastUdpClient == null) return;
        string message = $"PUZZLE_SHARE|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);
        SendBroadcast(data, "子网广播");
        SendBroadcast(data, "受限广播", true);
        if (lastLoggedBroadcastIP != GetBroadcastAddress())
        {
            lastLoggedBroadcastIP = GetBroadcastAddress();
            Log($"开始广播: {message}");
        }
    }

    public void NotifyClientsReady()
    {
        if (!isSharing) { Log("NotifyClientsReady: 未在分享中，忽略"); return; }
        Log($"开始发送就绪广播（{ReadyBroadcastDuration} 秒）");
        if (readyBroadcastCoroutine != null) StopCoroutine(readyBroadcastCoroutine);
        readyBroadcastCoroutine = StartCoroutine(PeriodicReadyBroadcast(ReadyBroadcastDuration));
    }

    private IEnumerator PeriodicReadyBroadcast(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            SendReadyBroadcastOnce();
            yield return new WaitForSeconds(ReadyBroadcastInterval);
            elapsed += ReadyBroadcastInterval;
        }
        readyBroadcastCoroutine = null;
        Log("就绪广播发送结束");
    }

    private void SendReadyBroadcastOnce()
    {
        if (!isSharing || broadcastUdpClient == null) return;
        string message = $"PUZZLE_SHARE_READY|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);
        SendBroadcast(data, "就绪广播(子网)");
        SendBroadcast(data, "就绪广播(受限)", true);
    }

    private void SendBroadcast(byte[] data, string logTag, bool useLimitedBroadcast = false)
    {
        try
        {
            IPEndPoint ep = useLimitedBroadcast
                ? new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)
                : new IPEndPoint(IPAddress.Parse(GetBroadcastAddress()), DiscoveryPort);
            broadcastUdpClient.Send(data, data.Length, ep);
        }
        catch (Exception e) { Log($"[错误] {logTag} 发送失败: {e.Message}"); }
    }

    public void StopBroadcast()
    {
        CancelInvoke(nameof(BroadcastPresence));
        if (readyBroadcastCoroutine != null) { StopCoroutine(readyBroadcastCoroutine); readyBroadcastCoroutine = null; }
        if (broadcastUdpClient != null) { try { broadcastUdpClient.Close(); } catch { } broadcastUdpClient = null; }
        if (autoStopCoroutine != null) { StopCoroutine(autoStopCoroutine); autoStopCoroutine = null; }
        Log("广播已停止");
    }

    public void StopSharing()
    {
        if (!isSharing && broadcastUdpClient == null && tcpListener == null) return;
        Log("========== 停止分享 ==========");
        isSharing = false;
        StopBroadcast();
        StopFileServer();
        clientConnected = false;
        OnSharingStopped?.Invoke();
    }

    private IEnumerator AutoStopSharingAfterTimeout(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (!clientConnected && isSharing)
        {
            Log("分享超时，自动停止");
            StopSharing();
        }
    }

    public void SetSharedFiles(List<string> files)
    {
        sharedFiles = files != null ? new List<string>(files) : new List<string>();
        Log($"[服务端] 已设置共享文件列表，共 {sharedFiles.Count} 个:");
        foreach (var f in sharedFiles) Log($"[服务端]   - {f}");
    }

    #endregion

    #region 发现方（客户端）

    public void StartDiscovery(Action<string, string, int, bool> onDeviceFound)
    {
        if (isDiscovering && discoveryUdpClient != null)
        {
            Log("已经在监听广播，跳过重复启动");
            return;
        }
        Log("========== 开始发现设备 ==========");
        StopDiscovery();

        try
        {
            isDiscovering = true;
            discoveryUdpClient = new UdpClient(DiscoveryPort);
            discoveryUdpClient.BeginReceive(OnDiscoveryReceive,
                new object[] { discoveryUdpClient, onDeviceFound });
            Log($"UDP 监听已启动，端口 {DiscoveryPort}");
        }
        catch (Exception e)
        {
            Log($"[错误] 启动发现失败: {e.Message}");
            isDiscovering = false;
            if (discoveryUdpClient != null) { try { discoveryUdpClient.Close(); } catch { } discoveryUdpClient = null; }
        }
    }

    private void OnDiscoveryReceive(IAsyncResult ar)
    {
        object[] args = (object[])ar.AsyncState;
        UdpClient client = (UdpClient)args[0];
        Action<string, string, int, bool> callback = (Action<string, string, int, bool>)args[1];

        try
        {
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = client.EndReceive(ar, ref remoteEP);
            string message = Encoding.UTF8.GetString(data);
            Log($"[接收广播] 来自 {remoteEP}: {message}");

            if (message.StartsWith("PUZZLE_SHARE_READY|"))
            {
                string[] parts = message.Split('|');
                if (parts.Length == 4)
                {
                    string devName = parts[1];
                    string ip = parts[2];
                    int port = int.Parse(parts[3]);
                    EnqueueMainThread(() => callback?.Invoke(devName, ip, port, true));
                }
            }
            else if (message.StartsWith("PUZZLE_SHARE|"))
            {
                string[] parts = message.Split('|');
                if (parts.Length == 4)
                {
                    string devName = parts[1];
                    string ip = parts[2];
                    int port = int.Parse(parts[3]);
                    EnqueueMainThread(() => callback?.Invoke(devName, ip, port, false));
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception e) { Log($"[错误] 接收广播异常: {e.Message}"); }

        if (isDiscovering && discoveryUdpClient != null)
        {
            try { client.BeginReceive(OnDiscoveryReceive, ar); }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Log($"[错误] 重新监听失败: {e.Message}"); }
        }
    }

    public void StopDiscovery()
    {
        if (!isDiscovering && discoveryUdpClient == null) return;
        Log("停止发现设备");
        isDiscovering = false;
        if (discoveryUdpClient != null) { try { discoveryUdpClient.Close(); } catch { } discoveryUdpClient = null; }
    }

    #endregion

    #region TCP 服务器

    private void StartFileServer()
    {
        try
        {
            tcpListener = new TcpListener(IPAddress.Any, FileTransferPort);
            tcpListener.Start();
            isServerRunning = true;
            tcpListener.BeginAcceptTcpClient(OnClientConnected, null);
            Log($"TCP 服务器已启动，监听端口 {FileTransferPort}");
        }
        catch (Exception e)
        {
            Log($"[错误] TCP 服务器启动失败: {e.Message}");
            isServerRunning = false;
            tcpListener = null;
        }
    }

    private void OnClientConnected(IAsyncResult ar)
    {
        if (!isServerRunning) return;
        TcpClient client = null;
        try { client = tcpListener.EndAcceptTcpClient(ar); }
        catch (Exception e) { Log($"[错误] EndAcceptTcpClient 异常: {e.Message}"); return; }

        try { client.NoDelay = true; } catch { }

        clientConnected = true;
        string remoteInfo = client.Client != null ? client.Client.RemoteEndPoint.ToString() : "unknown";
        Log($"[服务端] 客户端已连接: {remoteInfo}");

        TcpClient capturedClient = client;
        EnqueueMainThread(() =>
        {
            if (autoStopCoroutine != null)
            {
                StopCoroutine(autoStopCoroutine);
                autoStopCoroutine = null;
                Log("[服务端] 已取消自动停止协程");
            }

            System.Threading.Tasks.Task.Run(() => HandleClient(capturedClient));

            if (isServerRunning && tcpListener != null)
            {
                try { tcpListener.BeginAcceptTcpClient(OnClientConnected, null); }
                catch (Exception e) { Log($"[错误] 继续接受连接失败: {e.Message}"); }
            }
        });
    }

    private void HandleClient(TcpClient client)
    {
        Log("[服务端] ========== 客户端会话开始 ==========");
        Log($"[服务端] client.Connected={client.Connected}");

        NetworkStream stream = null;
        int loopCount = 0;

        // ★ 使用缓存的目录，避免在后台线程调用 Application.persistentDataPath
        string uploadsDir = uploadsDirectory;

        try
        {
            stream = client.GetStream();
            Log("[服务端] 已取得 NetworkStream，进入 while(true) 循环");

            while (true)
            {
                loopCount++;
                Log($"[服务端] ---- 第 {loopCount} 次 ReceiveMessage 开始 ----");
                Log($"[服务端] client.Connected={client.Connected}");

                byte msgType;
                byte[] payload;
                string reason;
                bool ok = ReceiveMessage(stream, out msgType, out payload, out reason);
                Log($"[服务端] ReceiveMessage 返回 ok={ok}, reason={reason ?? "null"}");

                if (!ok)
                {
                    Log($"[服务端] 读消息失败，准备 break");
                    break;
                }

                Log($"[服务端] 收到消息 type={msgType}, payloadLen={(payload?.Length ?? -1)}");

                if (msgType != MSG_TEXT)
                {
                    Log("[服务端] 非文本消息，忽略");
                    continue;
                }

                string request = Encoding.UTF8.GetString(payload);
                Log($"[服务端] 请求内容: {request}");

                if (request == "LIST")
                {
                    List<string> filesToSend = (sharedFiles != null && sharedFiles.Count > 0)
                        ? new List<string>(sharedFiles)
                        : new List<string>();

                    string json = JsonUtility.ToJson(new StringListWrapper(filesToSend));
                    Log($"[服务端] 准备发送列表 ({filesToSend.Count} 个)");
                    SendText(stream, json);
                    Log("[服务端] 列表已发送");
                }
                else if (request.StartsWith("GET|"))
                {
                    string fileName = request.Substring(4);
                    Log($"[服务端] 下载请求: {fileName}");

                    string filePath = Path.Combine(uploadsDir, fileName);
                    Log($"[服务端] 文件路径: {filePath}");

                    bool exists = File.Exists(filePath);
                    Log($"[服务端] 文件存在: {exists}");

                    if (exists)
                    {
                        byte[] data = File.ReadAllBytes(filePath);
                        Log($"[服务端] 准备发送 {data.Length} 字节");
                        SendFile(stream, data);
                        Log("[服务端] 文件已发送");
                    }
                    else
                    {
                        Log("[服务端] 文件不存在，发送 0 字节");
                        SendFile(stream, new byte[0]);
                    }
                }
                else
                {
                    Log($"[服务端] 未知命令: {request}");
                }
            }
        }
        catch (Exception e)
        {
            Log($"[错误] HandleClient 异常: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            Log($"[服务端] 进入 finally（loopCount={loopCount}）");
            try { if (stream != null) stream.Close(); } catch { }
            try { client.Close(); } catch { }
            Log("[服务端] ========== 客户端会话结束 ==========");
        }
    }

    private void StopFileServer()
    {
        isServerRunning = false;
        if (tcpListener != null)
        {
            try { tcpListener.Stop(); } catch { }
            tcpListener = null;
            Log("TCP 服务器已停止");
        }
    }

    #endregion

    #region TCP 客户端

    public void ConnectToServer(string serverIP, int port)
    {
        Log($"========== 尝试连接服务器 {serverIP}:{port} ==========");

        if (connectedClient != null && clientStream != null)
        {
            Log("已经连接，忽略");
            return;
        }

        DisconnectFromServer();

        TcpClient client = new TcpClient();
        try
        {
            IAsyncResult result = client.BeginConnect(serverIP, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(ConnectTimeoutMs);

            if (!success)
            {
                try { client.Close(); } catch { }
                Log($"[错误] 连接超时: {serverIP}:{port}");
                return;
            }

            client.EndConnect(result);
            try { client.NoDelay = true; } catch { }

            connectedClient = client;
            clientStream = connectedClient.GetStream();
            LastConnectedServerIP = serverIP;

            Log($"连接成功: {serverIP}:{port}");
        }
        catch (Exception e)
        {
            Log($"[错误] 连接失败: {e.Message}");
            try { client.Close(); } catch { }
            connectedClient = null;
            clientStream = null;
        }
    }

    public void DisconnectFromServer()
    {
        if (clientStream != null) { try { clientStream.Close(); } catch { } clientStream = null; }
        if (connectedClient != null) { try { connectedClient.Close(); } catch { } connectedClient = null; }
        // ★ 保留 LastConnectedServerIP 用于重连
    }

    private bool EnsureClientConnected()
    {
        if (clientStream != null)
        {
            try
            {
                if (clientStream.CanWrite && clientStream.CanRead) return true;
            }
            catch { }
        }

        string lastIP = LastConnectedServerIP;
        if (string.IsNullOrEmpty(lastIP))
        {
            Log("[错误] 缺少服务器 IP，无法重连");
            return false;
        }

        Log($"客户端连接不可用，尝试重连 {lastIP}:{FileTransferPort}");
        DisconnectFromServer();
        ConnectToServer(lastIP, FileTransferPort);
        return connectedClient != null && clientStream != null;
    }

    public void DownloadImageList(Action<List<string>> onListReceived)
    {
        Log("========== 请求远程图片列表 ==========");

        if (!EnsureClientConnected())
        {
            onListReceived?.Invoke(new List<string>());
            return;
        }

        try
        {
            SendText(clientStream, "LIST");
            Log("已发送 LIST");

            byte type;
            byte[] payload;
            string reason;
            if (!ReceiveMessage(clientStream, out type, out payload, out reason))
            {
                Log($"[错误] 读取列表响应失败: {reason}");
                DisconnectFromServer();
                onListReceived?.Invoke(new List<string>());
                return;
            }

            if (type != MSG_TEXT)
            {
                Log($"[错误] 期望 TEXT，收到 type={type}");
                onListReceived?.Invoke(new List<string>());
                return;
            }

            string json = Encoding.UTF8.GetString(payload);
            Log($"收到响应: {json}");

            var wrapper = JsonUtility.FromJson<StringListWrapper>(json);
            List<string> result = wrapper?.items ?? new List<string>();
            Log($"解析成功，共 {result.Count} 个文件");
            onListReceived?.Invoke(result);
        }
        catch (Exception e)
        {
            Log($"[错误] 获取列表失败: {e.Message}");
            DisconnectFromServer();
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
        Log($"========== 下载: {fileName} ==========");

        // 第一次尝试
        if (TryDownloadOnce(fileName, saveToShared, onDownloaded)) return;

        // 失败后重连一次
        Log("首次下载失败，重连后再试");
        DisconnectFromServer();

        if (!EnsureClientConnected())
        {
            Log("[错误] 重连失败，放弃");
            return;
        }

        if (!TryDownloadOnce(fileName, saveToShared, onDownloaded))
        {
            Log("[错误] 重试仍失败");
        }
    }

    private bool TryDownloadOnce(string fileName, bool saveToShared, Action<string> onDownloaded)
    {
        if (clientStream == null) return false;

        try
        {
            SendText(clientStream, $"GET|{fileName}");
            Log($"已发送 GET|{fileName}");

            byte type;
            byte[] payload;
            string reason;
            if (!ReceiveMessage(clientStream, out type, out payload, out reason))
            {
                Log($"[错误] 读响应失败: {reason}");
                return false;
            }

            if (type != MSG_FILE)
            {
                Log($"[错误] 期望 FILE，收到 type={type}");
                return false;
            }

            if (payload == null || payload.Length == 0)
            {
                Log("[错误] 服务端返回 0 字节");
                return false;
            }

            // ★ 使用缓存目录，避免调用 Application.persistentDataPath
            string targetDir = saveToShared ? sharedDirectory : uploadsDirectory;
            Directory.CreateDirectory(targetDir);
            string destPath = Path.Combine(targetDir, fileName);
            File.WriteAllBytes(destPath, payload);
            Log($"文件已保存: {destPath}");

            string fn = fileName;
            bool s = saveToShared;
            EnqueueMainThread(() =>
            {
                if (s) GameDataManager.AddSharedImage(fn);
                else GameDataManager.AddUploadedImage(fn);
            });

            onDownloaded?.Invoke(destPath);
            return true;
        }
        catch (Exception e)
        {
            Log($"[错误] 下载异常: {e.Message}");
            return false;
        }
    }

    #endregion

    #region 工具方法

    private string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip) &&
                    !ip.ToString().StartsWith("169.254") &&
                    !ip.ToString().StartsWith("192.168.56") &&
                    !ip.ToString().StartsWith("192.168.99"))
                    return ip.ToString();
            }
            return "";
        }
        catch (Exception e)
        {
            Log($"[错误] 获取本机IP失败: {e.Message}");
            return "";
        }
    }

    private string GetBroadcastAddress()
    {
        string localIP = GetLocalIPAddress();
        if (string.IsNullOrEmpty(localIP)) return "255.255.255.255";
        string[] parts = localIP.Split('.');
        if (parts.Length != 4) return "255.255.255.255";
        return parts[0] + "." + parts[1] + "." + parts[2] + ".255";
    }

    #endregion

    #region 辅助数据结构

    [Serializable]
    public class StringListWrapper
    {
        public List<string> items;
        public StringListWrapper() { }
        public StringListWrapper(List<string> items) { this.items = items; }
    }

    #endregion
}