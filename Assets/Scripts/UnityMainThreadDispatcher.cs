using System;                    // Action 委托、Queue 等 .NET 基础类型
using System.Collections;        // 非泛型集合（实际本脚本未使用，可以删）
using System.Collections.Generic;// 泛型集合：Queue<Action>
using UnityEngine;               // Unity API

/// <summary>
/// Unity 主线程调度器。
///
/// 【为什么需要这个脚本？】
/// Unity 有一个"铁律"：
///   所有 Unity API（访问 GameObject、Transform、Text、Image 等）
///   只能在主线程里调用，不能在后台线程里调用。
/// 违反这条规则，会报错：
///   "xxx can only be called from the main thread"
///   或者直接崩溃。
///
/// 但很多操作天然在后台线程：
///   - TCP / UDP 网络回调
///   - System.Threading.Tasks.Task.Run(...)
///   - new Thread(...)
/// 这些地方拿到了数据，想更新 UI，就必须"回到主线程"再执行。
///
/// 【本脚本做的事情】
/// 提供一个"任务队列"：
///   后台线程：把要做的事 Enqueue 进队列（随便哪个线程调用都行）
///   主线程：每一帧 Update 里取出队列里的任务，在主线程执行
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
    /// 后台线程先加入的任务，先被主线程执行，符合直觉。
    /// List 也行，但 Queue 的语义更贴合"队列"场景。
    ///
    /// 【Action 是什么？】
    /// Action 是 C# 内置的"无参数无返回值的方法"类型。
    /// 可以理解成"一个可以执行的代码块"。
    /// 例：() => { text.text = "hello"; }
    /// </summary>
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

    /// <summary>
    /// 单例实例。
    /// 使用 static 保证全局唯一。
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
    /// 因为 new GameObject 只能在主线程做，
    /// 所以这里的 get 只能在主线程访问。
    /// 后台线程不要碰 Instance，直接调用 _executionQueue 需要的方法。
    /// （本脚本为了方便，还是让后台线程通过 Instance.Enqueue 来入队，
    ///   但前提是 Instance 已经在主线程访问过至少一次，被初始化过。）
    /// </summary>
    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            // 如果还没初始化过
            if (_instance == null)
            {
                // 尝试在场景里找有没有现成的
                // FindFirstObjectByType 是 Unity 6 的新 API，比 FindObjectOfType 更快
                _instance = FindFirstObjectByType<UnityMainThreadDispatcher>();

                // 还没找到（场景里没挂），就自动创建一个
                if (_instance == null)
                {
                    // 创建一个新 GameObject
                    var go = new GameObject("UnityMainThreadDispatcher");

                    // 把本脚本挂上去
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();

                    // 让它跨场景常驻（避免切场景后队列丢失）
                    DontDestroyOnLoad(go);
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
        // 空引用直接忽略，避免后面执行时抛异常
        if (action == null) return;

        // ========== 关键：加锁 ==========
        // lock 会保证"同一时间只有一个线程能进入这个代码块"。
        //
        // 【为什么需要锁？】
        // Enqueue 可能在多个后台线程同时调用，
        // 而 Update 也会在主线程读取队列。
        // 如果不加锁，可能出现：
        //   - 队列正在 Enqueue 时被 Update 修改 → 数据错乱
        //   - 队列内部索引异常 → 抛异常或死循环
        //
        // 加锁后，一次只有一个线程能操作队列，天然安全。
        //
        // 【lock 的对象怎么选？】
        // 惯例是 new 一个专用的 object 作为"锁标识"。
        // 本脚本用 _executionQueue 本身当锁标识也可以，
        // 因为它是个引用类型（不能是值类型）。
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
    /// 【为什么 Update 可以执行 Unity API？】
    /// 因为 Update 是 Unity 自动在主线程调用的，
    /// 所以里面执行的代码天然在主线程上。
    /// 
    /// 【执行逻辑】
    /// 用 while(true) 循环，每次从队列取一个任务执行。
    /// 队列空了就 break 退出循环。
    /// 这样一帧内可以把所有排队的任务都处理完。
    /// </summary>
    private void Update()
    {
        // 循环直到队列为空
        while (true)
        {
            Action action = null;

            // 加锁从队列取出一个任务
            lock (_executionQueue)
            {
                // 队列里还有任务就取一个出来
                if (_executionQueue.Count > 0)
                    action = _executionQueue.Dequeue();
            }
            // ↑ 加锁范围尽量小，只保护队列操作，
            //   真正的"执行任务"放在锁外面，
            //   避免任务执行时占用锁（可能导致其他线程 Enqueue 阻塞）。

            // 队列已空，退出循环
            if (action == null) break;

            // 在主线程中执行任务
            // 用 try-catch 包一下，避免一个任务抛异常导致后面的任务全部卡住
            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MainThreadDispatcher] 任务执行异常: {e}");
            }
        }
    }

    #endregion
}