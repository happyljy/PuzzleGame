using System;                     // Action 委托
using System.Collections.Generic; // Queue<Action>
using UnityEngine;                // Unity API

/// <summary>
/// Unity 主线程调度器。
///
/// 【为什么需要这个脚本？】
/// Unity 有一条铁律：
///   所有 Unity API（访问 GameObject、Transform、Text、Image 等）
///   只能在主线程里调用，不能在后台线程里调用。
/// 违反这条规则，会报错：
///   "xxx can only be called from the main thread"
///   严重时直接崩溃。
///
/// 但很多操作天然发生在后台线程：
///   - TCP / UDP 网络回调
///   - System.Threading.Tasks.Task.Run(...)
///   - new Thread(...)
/// 这些地方拿到数据后想更新 UI，就必须"回到主线程"再执行。
///
/// 【本脚本做什么？】
/// 提供一个线程安全的任务队列：
///   后台线程：把要做的事 Enqueue 进队列（任意线程都可调用）
///   主线程：每帧 Update 里取出队列中的任务依次执行
///
/// 这样就实现了"跨线程安全地更新 UI"。
///
/// 【使用示例】
/// <code>
/// // 在后台线程里（如 TCP 回调）
/// UnityMainThreadDispatcher.Instance.Enqueue(() => {
///     someText.text = "下载完成";        // 这句在主线程执行
///     someImage.sprite = loadedSprite;
/// });
/// </code>
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    #region 静态字段与单例

    /// <summary>
    /// 待执行的任务队列（线程安全）。
    ///
    /// 【为什么是 static？】
    /// static 表示这个队列属于"类"而不是"实例"，
    /// 全局只有一份，任何代码都可以访问。
    ///
    /// 【为什么用 Queue 而不是 List？】
    /// Queue 是"先进先出"（FIFO）结构。
    /// 后台线程先加入的任务先被主线程执行，符合直觉。
    /// List 也能用，但 Queue 的语义更贴合"队列"场景。
    ///
    /// 【Action 是什么？】
    /// Action 是 C# 内置的"无参数、无返回值的方法"类型。
    /// 可以理解成"一个可以执行的代码块"。
    /// 例：() => { text.text = "hello"; }
    /// </summary>
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

    /// <summary>
    /// 单例实例。使用 static 保证全局唯一。
    /// </summary>
    private static UnityMainThreadDispatcher _instance = null;

    /// <summary>
    /// 获取单例。
    ///
    /// 【自动创建机制】
    /// 如果场景里还没挂这个脚本，第一次访问 Instance 时会自动：
    ///   1. 尝试在场景里找已有的实例
    ///   2. 找不到就创建一个新 GameObject 并挂上本脚本
    /// 这样调用方不用手动摆物体，一句
    /// UnityMainThreadDispatcher.Instance.Enqueue(...)
    /// 就能用。
    ///
    /// 【注意】
    /// new GameObject 只能在主线程做，
    /// 所以这里只能在主线程访问。
    /// 后台线程不要碰 Instance，
    /// 前提是 Instance 已经在主线程被初始化过。
    /// </summary>
    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                // FindFirstObjectByType 是 Unity 6 的新 API，比 FindObjectOfType 更快
                _instance = FindFirstObjectByType<UnityMainThreadDispatcher>();

                if (_instance == null)
                {
                    var go = new GameObject("UnityMainThreadDispatcher");
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
                    DontDestroyOnLoad(go);   // 跨场景常驻
                }
            }
            return _instance;
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 将任务加入队列，等待主线程在 Update 中依次执行。
    /// 可从任意线程调用。
    /// </summary>
    /// <param name="action">要执行的操作（用 lambda 传入）</param>
    public void Enqueue(Action action)
    {
        if (action == null) return;

        // ========== 关键：加锁 ==========
        // lock 保证"同一时间只有一个线程能进入这个代码块"。
        //
        // 【为什么需要锁？】
        // Enqueue 可能在多个后台线程同时调用，
        // 而 Update 也会在主线程读取队列。
        // 不加锁可能出现：
        //   - 队列正在 Enqueue 时被 Update 修改 → 数据错乱
        //   - 队列内部索引异常 → 抛异常或死循环
        //
        // 【lock 的对象怎么选？】
        // 惯例是 new 一个专用 object 作为"锁标识"。
        // 这里用 _executionQueue 本身当锁标识也可以，
        // 因为它是个引用类型（lock 不能锁值类型）。
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// 每帧检查并执行队列中的任务（仅主线程执行）。
    ///
    /// 【为什么 Update 里能安全调用 Unity API？】
    /// Update 由 Unity 在主线程自动调用，
    /// 里面的代码天然在主线程上运行。
    ///
    /// 【执行逻辑】
    /// 用 while(true) 循环，每次从队列取一个任务执行，
    /// 队列空了就 break。
    /// 这样一帧内可以把所有排队的任务全部处理完。
    /// </summary>
    private void Update()
    {
        // ★ 快速路径：队列为空直接返回，省掉一次 lock 开销
        // 说明：Queue.Count 在多线程下不是绝对安全，但最坏情况
        // 也只是读到旧值多进一次循环，不影响正确性。
        if (_executionQueue.Count == 0) return;

        while (true)
        {
            Action action = null;

            // 加锁从队列取出一个任务
            lock (_executionQueue)
            {
                if (_executionQueue.Count > 0)
                    action = _executionQueue.Dequeue();
            }

            // 队列已空，退出
            if (action == null) break;

            // 在主线程执行任务
            // 用 try-catch 防止单个任务出错导致后面的任务全部卡住
            try { action.Invoke(); }
            catch (Exception e) { Debug.LogError($"[MainThreadDispatcher] 任务执行异常: {e}"); }
        }
    }

    #endregion
}