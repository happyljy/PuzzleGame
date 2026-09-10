using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unity 主线程调度器：用于将后台线程（如 TCP、UDP 回调）中的操作
/// 安全地调度到主线程执行（例如更新 UI、调用 Unity API）。
/// 
/// 使用示例：
/// <code>
/// UnityMainThreadDispatcher.Instance.Enqueue(() => {
///     // 这里可以安全地访问 Unity 组件
/// });
/// </code>
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    #region 静态字段与单例

    /// <summary>待执行的任务队列（线程安全）。</summary>
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

    /// <summary>单例实例。</summary>
    private static UnityMainThreadDispatcher _instance = null;

    /// <summary>
    /// 获取单例。如果不存在，会自动创建一个常驻 GameObject 挂载此脚本。
    /// </summary>
    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<UnityMainThreadDispatcher>();
                if (_instance == null)
                {
                    var go = new GameObject("UnityMainThreadDispatcher");
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
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
    /// <param name="action">要执行的操作</param>
    public void Enqueue(Action action)
    {
        if (action == null) return;

        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// 每帧检查并执行队列中的任务（仅主线程执行）。
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
                if (_executionQueue.Count > 0)
                    action = _executionQueue.Dequeue();
            }

            // 队列已空，退出循环
            if (action == null) break;

            // 在主线程中执行
            action.Invoke();
        }
    }

    #endregion
}