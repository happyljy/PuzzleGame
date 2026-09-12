using System;                         // Action、Exception、Serializable
using System.Collections;             // IEnumerator（协程）
using System.Collections.Generic;     // List、Queue
using System.IO;                      // File、Path、Directory
using System.Net;                     // IPAddress、IPEndPoint、Dns
using System.Net.Sockets;             // TcpListener、TcpClient、UdpClient、NetworkStream
using System.Text;                    // Encoding.UTF8
using UnityEngine;                    // Unity API

/// <summary>
/// 局域网分享管理器。
///
/// 【功能概述】
/// 让同一 Wi-Fi 下的两台设备互传图片：
///   1. 服务端（分享方）：启动 TCP 服务器 + UDP 广播，等待客户端连接
///   2. 客户端（接收方）：监听 UDP 广播，发现服务端，连接下载图片
///
/// 【通信协议】
/// 所有消息统一为：[1字节类型][4字节长度][payload]
///   类型 0x01 = 文本消息（UTF-8 字符串）
///   类型 0x02 = 文件消息（二进制数据）
///
/// 为什么自定义协议？
///   TCP 是"字节流"，没有包边界。
///   发 "AB" 和 "CD" 两次，接收方可能一次收到 "ABCD"。
///   所以在数据前加"长度头"，告诉对方这条消息有多少字节。
///
/// 【线程模型】
/// 后台线程（UDP/TCP 回调）不能调用 Unity API。
/// 本脚本通过两个队列桥接：
///   - logQueue：后台线程写日志 → 主线程刷 UI
///   - mainThreadActions：后台线程投递任务 → 主线程执行
///
/// Unity API 值（如 persistentDataPath）在 Awake 缓存，
/// 后台线程直接读缓存字段，不再调用 Unity API。
/// </summary>
public class LANShareManager : MonoBehaviour
{
    #region 单例

    /// <summary>全局唯一实例。</summary>
    public static LANShareManager Instance { get; private set; }

    #endregion

    #region 常量

    /// <summary>UDP 广播端口。服务端往这个端口发广播，客户端监听这个端口。</summary>
    private const int DiscoveryPort = 8888;

    /// <summary>TCP 文件传输端口。客户端连到这个端口下载文件。</summary>
    private const int FileTransferPort = 5555;

    /// <summary>连接超时时间（毫秒）。3 秒内没连上就放弃。</summary>
    private const int ConnectTimeoutMs = 3000;

    /// <summary>分享超时时间（秒）。10 分钟无客户端连接自动停止。</summary>
    private const float AutoStopSharingTimeout = 600f;

    /// <summary>就绪广播持续时间（秒）。</summary>
    private const float ReadyBroadcastDuration = 120f;

    /// <summary>就绪广播发送间隔（秒）。</summary>
    private const float ReadyBroadcastInterval = 1.5f;

    /// <summary>消息类型：文本（UTF-8 字符串）。</summary>
    private const byte MSG_TEXT = 0x01;

    /// <summary>消息类型：文件（二进制数据）。</summary>
    private const byte MSG_FILE = 0x02;

    /// <summary>单条消息的最大长度（16 MB），防止超长长度头导致内存爆炸。</summary>
    private const int MaxPayloadSize = 16 * 1024 * 1024;

    #endregion

    #region 对外属性与事件

    /// <summary>服务端：是否有客户端已连接。</summary>
    public bool ClientConnected => clientConnected;

    /// <summary>客户端：是否已连接到服务端。</summary>
    public bool ConnectedToServer => connectedClient != null && clientStream != null;

    /// <summary>客户端：最近一次连接的服务端 IP，用于自动重连。</summary>
    public string LastConnectedServerIP { get; set; }

    /// <summary>分享停止事件，外部可订阅更新 UI。</summary>
    public event Action OnSharingStopped;

    #endregion

    #region 私有字段

    // ---------- 缓存的 Unity API ----------
    // 后台线程不能访问 Application.persistentDataPath，
    // 所以在 Awake 主线程里提前缓存。
    private string persistentDataPath;
    private string uploadsDirectory;    // 上传图片目录
    private string sharedDirectory;     // 共享图片目录

    // ---------- UDP 客户端 ----------
    private UdpClient broadcastUdpClient;    // 服务端：发广播
    private UdpClient discoveryUdpClient;    // 客户端：收广播

    // ---------- 状态标记 ----------
    private bool isDiscovering = false;      // 是否正在发现设备
    private bool isSharing = false;          // 是否正在分享
    private bool isServerRunning = false;    // TCP 服务器是否运行中

    // ---------- TCP 连接 ----------
    private TcpListener tcpListener;         // 服务端：监听连接
    private TcpClient connectedClient;       // 客户端：已连接 socket
    private NetworkStream clientStream;      // 客户端：网络流（缓存引用防止被 GC 关闭）

    // ---------- 分享数据 ----------
    private List<string> sharedFiles = new List<string>();
    private string deviceName;

    /// <summary>
    /// 客户端是否已连接。
    /// volatile 保证多线程读到的都是最新值。
    /// 不加可能读到缓存旧值，导致 while 循环死等。
    /// </summary>
    private volatile bool clientConnected = false;

    // ---------- 协程引用 ----------
    private Coroutine autoStopCoroutine;         // 超时自动停止
    private Coroutine readyBroadcastCoroutine;   // 就绪广播

    // ---------- 调试日志队列 ----------
    // 后台线程写日志 → 主线程 Update 里出队写 UI
    private readonly Queue<string> logQueue = new Queue<string>();
    private readonly object logQueueLock = new object();

    // ---------- 主线程任务队列 ----------
    // 后台线程想做 Unity 相关操作（协程、PlayerPrefs），
    // 先塞进这个队列，主线程 Update 里执行
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly object mainThreadActionsLock = new object();

    /// <summary>上次打印过的广播地址，避免每 2 秒刷屏日志。</summary>
    private string lastLoggedBroadcastIP = "";

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake：单例保护 + 缓存 Unity API。
    /// </summary>
    private void Awake()
    {
        // ---------- 单例保护 ----------
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // ---------- 缓存设备名 ----------
        deviceName = SystemInfo.deviceName;

        // ---------- 缓存 Unity API（关键） ----------
        // 后台线程访问这些路径时，直接用字段，不调用 Application.xxx
        persistentDataPath = Application.persistentDataPath;
        uploadsDirectory = Path.Combine(persistentDataPath, "Uploads");
        sharedDirectory = Path.Combine(persistentDataPath, "Shared");

        Log($"LANShareManager 初始化，deviceName={deviceName}");
        Log($"persistentDataPath = {persistentDataPath}");
    }

    /// <summary>OnDestroy：清理所有网络资源。</summary>
    private void OnDestroy()
    {
        StopSharing();
        StopDiscovery();
        DisconnectFromServer();
        StopFileServer();
    }

    /// <summary>
    /// Update：主线程处理队列。
    ///   1. 执行后台线程调度过来的任务
    ///   2. 把日志刷到 UI
    /// </summary>
    private void Update()
    {
        // ★ 快速路径：两个队列都空就立即返回，避免每帧拿锁
        if (logQueue.Count == 0 && mainThreadActions.Count == 0)
            return;

        // 1. 主线程任务
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

        // 2. 日志出队刷 UI
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

    #region 协议实现（收发消息）

    /// <summary>
    /// 发送文本消息。
    /// 格式：[0x01][4字节长度][UTF-8 字符串]
    /// </summary>
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

    /// <summary>
    /// 发送文件消息。
    /// 格式：[0x02][4字节长度][二进制内容]
    /// </summary>
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
    /// 读一条消息。
    /// </summary>
    /// <param name="type">输出参数：消息类型</param>
    /// <param name="payload">输出参数：消息内容</param>
    /// <param name="reason">输出参数：失败原因（成功时 null）</param>
    private static bool ReceiveMessage(NetworkStream stream, out byte type, out byte[] payload, out string reason)
    {
        type = 0;
        payload = null;
        reason = null;

        // 读 5 字节头
        byte[] header = new byte[5];
        int headerRead = ReadExact(stream, header, 5);
        if (headerRead != 5)
        {
            reason = $"header 读取失败 ({headerRead}/5 字节)，对端可能已关闭";
            return false;
        }

        // 解析类型和长度
        type = header[0];
        int len = BitConverter.ToInt32(header, 1);

        if (len < 0 || len > MaxPayloadSize)
        {
            reason = $"非法长度 {len}";
            return false;
        }

        // 读 payload
        payload = new byte[len];
        int payloadRead = ReadExact(stream, payload, len);
        if (payloadRead != len)
        {
            reason = $"payload 读取失败 ({payloadRead}/{len} 字节)";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 精确读满 count 字节。
    /// TCP 是流式，一次 Read 可能只返回一部分，要循环读。
    /// 返回实际读到的字节数。
    /// </summary>
    private static int ReadExact(NetworkStream stream, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = stream.Read(buf, total, count - total); }
            catch { return total; }

            if (read <= 0) return total;   // 返回 0 表示对端关闭
            total += read;
        }
        return total;
    }

    #endregion

    #region 调试辅助（线程安全）

    /// <summary>
    /// 记录日志（线程安全）。
    /// 只入队，UI 写入由 Update 完成。
    /// </summary>
    private void Log(string msg)
    {
        string line = $"[LAN] {msg}";
        lock (logQueueLock)
        {
            if (logQueue.Count > 800) logQueue.Dequeue();
            logQueue.Enqueue(line);
        }
    }

    /// <summary>
    /// 把操作调度到主线程执行。
    /// 用于后台线程回调里做 Unity 相关操作。
    /// </summary>
    private void EnqueueMainThread(Action action)
    {
        if (action == null) return;
        lock (mainThreadActionsLock) mainThreadActions.Enqueue(action);
    }

    #endregion

    #region 分享方（服务端）

    /// <summary>
    /// 启动分享：启动 TCP 服务器 + 开始周期广播。
    /// </summary>
    public void StartSharing()
    {
        if (isSharing) { Log("已经在分享中，忽略"); return; }
        isSharing = true;

        Log("========== 开始分享 ==========");
        Log($"本机IP: {GetLocalIPAddress()}");
        Log($"广播地址: {GetBroadcastAddress()}");
        Log($"设备名: {deviceName}");

        // 启动 TCP 服务器
        StartFileServer();

        // 创建 UDP 广播客户端
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

        // 每 2 秒广播一次
        InvokeRepeating(nameof(BroadcastPresence), 0f, 2f);

        clientConnected = false;
        lastLoggedBroadcastIP = "";

        // 启动超时自动停止协程
        if (autoStopCoroutine != null) StopCoroutine(autoStopCoroutine);
        autoStopCoroutine = StartCoroutine(AutoStopSharingAfterTimeout(AutoStopSharingTimeout));

        Log("分享已启动，等待客户端连接");
    }

    /// <summary>
    /// 广播"我在这里"（常规消息）。
    /// 格式：PUZZLE_SHARE|设备名|IP|端口
    /// </summary>
    private void BroadcastPresence()
    {
        if (broadcastUdpClient == null) return;

        string message = $"PUZZLE_SHARE|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        // 同时发子网广播和受限广播，提高到达率
        SendBroadcast(data, "子网广播");
        SendBroadcast(data, "受限广播", true);

        // 只在广播地址变化时打印
        if (lastLoggedBroadcastIP != GetBroadcastAddress())
        {
            lastLoggedBroadcastIP = GetBroadcastAddress();
            Log($"开始广播: {message}");
        }
    }

    /// <summary>
    /// 通知客户端"我准备好了"。
    /// 用户点【分享】按钮时调用，开始周期性发 READY 广播。
    /// </summary>
    public void NotifyClientsReady()
    {
        if (!isSharing) { Log("NotifyClientsReady: 未在分享中，忽略"); return; }

        Log($"开始发送就绪广播（{ReadyBroadcastDuration} 秒）");

        if (readyBroadcastCoroutine != null) StopCoroutine(readyBroadcastCoroutine);
        readyBroadcastCoroutine = StartCoroutine(PeriodicReadyBroadcast(ReadyBroadcastDuration));
    }

    /// <summary>周期性发送就绪广播。</summary>
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

    /// <summary>
    /// 发送一次就绪广播。
    /// 格式：PUZZLE_SHARE_READY|设备名|IP|端口
    /// </summary>
    private void SendReadyBroadcastOnce()
    {
        if (!isSharing || broadcastUdpClient == null) return;

        string message = $"PUZZLE_SHARE_READY|{deviceName}|{GetLocalIPAddress()}|{FileTransferPort}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        SendBroadcast(data, "就绪广播(子网)");
        SendBroadcast(data, "就绪广播(受限)", true);
    }

    /// <summary>
    /// 发送广播数据。
    /// </summary>
    /// <param name="useLimitedBroadcast">
    /// true  → 255.255.255.255（受限广播）
    /// false → 本网段广播地址（如 192.168.1.255）
    /// </param>
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

    /// <summary>停止广播（不停止 TCP 服务器）。</summary>
    public void StopBroadcast()
    {
        CancelInvoke(nameof(BroadcastPresence));

        if (readyBroadcastCoroutine != null) { StopCoroutine(readyBroadcastCoroutine); readyBroadcastCoroutine = null; }

        if (broadcastUdpClient != null) { try { broadcastUdpClient.Close(); } catch { } broadcastUdpClient = null; }

        if (autoStopCoroutine != null) { StopCoroutine(autoStopCoroutine); autoStopCoroutine = null; }

        Log("广播已停止");
    }

    /// <summary>停止分享（广播 + TCP 服务器）。</summary>
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

    /// <summary>超时自动停止分享。</summary>
    private IEnumerator AutoStopSharingAfterTimeout(float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (!clientConnected && isSharing)
        {
            Log("分享超时，自动停止");
            StopSharing();
        }
    }

    /// <summary>
    /// 设置要分享的文件列表。由 UI 调用。
    /// </summary>
    public void SetSharedFiles(List<string> files)
    {
        // 拷贝一份，避免外部修改影响内部
        sharedFiles = files != null ? new List<string>(files) : new List<string>();
        Log($"[服务端] 已设置共享文件列表，共 {sharedFiles.Count} 个:");
        foreach (var f in sharedFiles) Log($"[服务端]   - {f}");
    }

    #endregion

    #region 发现方（客户端）

    /// <summary>
    /// 开始发现设备。
    /// </summary>
    /// <param name="onDeviceFound">回调：设备名, IP, 端口, 是否就绪广播</param>
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

            // BeginReceive 异步接收，回调参数带 client 和 callback
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

    /// <summary>
    /// 收到 UDP 广播的回调。
    /// 
    /// 【运行线程】.NET 线程池（后台线程），不能直接调 Unity API。
    /// </summary>
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
                    // ★ 回调必须在主线程执行（里面会更新 UI）
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
        catch (ObjectDisposedException)
        {
            // 停止监听时触发，正常现象
        }
        catch (Exception e) { Log($"[错误] 接收广播异常: {e.Message}"); }

        // 继续监听下一条
        if (isDiscovering && discoveryUdpClient != null)
        {
            try { client.BeginReceive(OnDiscoveryReceive, ar); }
            catch (ObjectDisposedException) { }
            catch (Exception e) { Log($"[错误] 重新监听失败: {e.Message}"); }
        }
    }

    /// <summary>停止发现设备。</summary>
    public void StopDiscovery()
    {
        if (!isDiscovering && discoveryUdpClient == null) return;

        Log("停止发现设备");
        isDiscovering = false;
        if (discoveryUdpClient != null)
        {
            try { discoveryUdpClient.Close(); } catch { }
            discoveryUdpClient = null;
        }
    }

    #endregion

    #region TCP 服务器

    /// <summary>启动 TCP 服务器。</summary>
    private void StartFileServer()
    {
        try
        {
            tcpListener = new TcpListener(IPAddress.Any, FileTransferPort);
            tcpListener.Start();
            isServerRunning = true;

            // 异步等待客户端连接
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

    /// <summary>
    /// 客户端连接后的回调。
    /// 【运行线程】.NET 线程池（后台线程）。
    /// </summary>
    private void OnClientConnected(IAsyncResult ar)
    {
        if (!isServerRunning) return;

        TcpClient client = null;
        try { client = tcpListener.EndAcceptTcpClient(ar); }
        catch (Exception e) { Log($"[错误] EndAcceptTcpClient 异常: {e.Message}"); return; }

        // 关闭 Nagle 算法，减少小包延迟
        try { client.NoDelay = true; } catch { }

        clientConnected = true;
        string remoteInfo = client.Client != null ? client.Client.RemoteEndPoint.ToString() : "unknown";
        Log($"[服务端] 客户端已连接: {remoteInfo}");

        // ★ 把 Unity API 调用调度到主线程
        TcpClient capturedClient = client;
        EnqueueMainThread(() =>
        {
            // 取消超时自动停止
            if (autoStopCoroutine != null)
            {
                StopCoroutine(autoStopCoroutine);
                autoStopCoroutine = null;
                Log("[服务端] 已取消自动停止协程");
            }

            // 在后台线程处理客户端会话
            System.Threading.Tasks.Task.Run(() => HandleClient(capturedClient));

            // 继续等待下一个客户端
            if (isServerRunning && tcpListener != null)
            {
                try { tcpListener.BeginAcceptTcpClient(OnClientConnected, null); }
                catch (Exception e) { Log($"[错误] 继续接受连接失败: {e.Message}"); }
            }
        });
    }

    /// <summary>
    /// 处理客户端会话（后台线程）。
    /// 循环接收请求：LIST（获取列表）/ GET|xxx（下载文件）
    /// </summary>
    private void HandleClient(TcpClient client)
    {
        Log("[服务端] ========== 客户端会话开始 ==========");
        Log($"[服务端] client.Connected={client.Connected}");

        NetworkStream stream = null;
        int loopCount = 0;

        // ★ 缓存目录（后台线程不能访问 Application.persistentDataPath）
        string uploadsDir = uploadsDirectory;

        try
        {
            stream = client.GetStream();
            Log("[服务端] 已取得 NetworkStream，进入 while(true) 循环");

            while (true)
            {
                loopCount++;
                Log($"[服务端] ---- 第 {loopCount} 次 ReceiveMessage 开始 ----");

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

                // 服务端只处理文本命令
                if (msgType != MSG_TEXT)
                {
                    Log("[服务端] 非文本消息，忽略");
                    continue;
                }

                string request = Encoding.UTF8.GetString(payload);
                Log($"[服务端] 请求内容: {request}");

                // ---------- LIST：返回文件列表 ----------
                if (request == "LIST")
                {
                    List<string> filesToSend = (sharedFiles != null && sharedFiles.Count > 0)
                        ? new List<string>(sharedFiles)
                        : new List<string>();

                    string json = JsonUtility.ToJson(new StringListWrapper(filesToSend));
                    SendText(stream, json);
                    Log($"[服务端] 已发送文件列表 ({filesToSend.Count} 个): {json}");
                }
                // ---------- GET|filename：返回文件内容 ----------
                else if (request.StartsWith("GET|"))
                {
                    string fileName = request.Substring(4);
                    Log($"[服务端] 下载请求: {fileName}");

                    string filePath = Path.Combine(uploadsDir, fileName);
                    bool exists = File.Exists(filePath);
                    Log($"[服务端] 文件路径: {filePath}, 存在: {exists}");

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

    /// <summary>停止 TCP 服务器。</summary>
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

    /// <summary>连接到服务端。</summary>
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
            // 异步连接 + 超时
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
            // ★ 缓存 stream，防止每次 GetStream 都拿新引用被 GC 关闭
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

    /// <summary>
    /// 断开与服务端的连接。
    /// 保留 LastConnectedServerIP，方便下次自动重连。
    /// </summary>
    public void DisconnectFromServer()
    {
        if (clientStream != null) { try { clientStream.Close(); } catch { } clientStream = null; }
        if (connectedClient != null) { try { connectedClient.Close(); } catch { } connectedClient = null; }
    }

    /// <summary>检查客户端连接是否可用，不可用则自动重连。</summary>
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

    /// <summary>请求服务端的文件列表。</summary>
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

    /// <summary>下载图片到 Uploads 目录。</summary>
    public void DownloadImage(string fileName, Action<string> onDownloaded)
    {
        DownloadImageInternal(fileName, false, onDownloaded);
    }

    /// <summary>下载图片到 Shared 目录。</summary>
    public void DownloadImageToShared(string fileName, Action<string> onDownloaded)
    {
        DownloadImageInternal(fileName, true, onDownloaded);
    }

    /// <summary>下载图片的内部实现（带自动重连）。</summary>
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

    /// <summary>单次下载尝试。</summary>
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

            // ★ 用缓存目录，避免调用 Application.persistentDataPath
            string targetDir = saveToShared ? sharedDirectory : uploadsDirectory;
            Directory.CreateDirectory(targetDir);
            string destPath = Path.Combine(targetDir, fileName);
            File.WriteAllBytes(destPath, payload);
            Log($"文件已保存: {destPath}");

            // 写 PlayerPrefs 必须在主线程
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

    /// <summary>
    /// 获取本机局域网 IP。
    /// 过滤回环、自动配置、虚拟网卡等，只返回真实局域网 IP。
    /// </summary>
    private string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                // 过滤条件：
                // - IPv4（不是 IPv6）
                // - 不是回环 127.0.0.1
                // - 不是 169.254.x.x（自动配置失败）
                // - 不是 192.168.56.x（VirtualBox）
                // - 不是 192.168.99.x（Docker）
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

    /// <summary>
    /// 根据本机 IP 计算子网广播地址。
    /// 例：192.168.1.5 → 192.168.1.255
    /// </summary>
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

    /// <summary>
    /// JSON 序列化包装类。
    /// Unity 的 JsonUtility 不支持直接序列化 List，
    /// 必须套在一个类的字段里才行。
    /// 用法：JsonUtility.ToJson(new StringListWrapper(list))
    /// 结果：{"items":["a","b","c"]}
    /// </summary>
    [Serializable]
    public class StringListWrapper
    {
        public List<string> items;

        // 无参构造（JsonUtility 反序列化用）
        public StringListWrapper() { }

        // 带参构造（我们自己创建时用）
        public StringListWrapper(List<string> items) { this.items = items; }
    }

    #endregion
}