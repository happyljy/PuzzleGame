using UnityEngine;                 // Unity 基础 API
using UnityEngine.EventSystems;    // 事件系统（PointerEventData、IPointerDownHandler）
using UnityEngine.UI;              // UI 组件（Button）

/// <summary>
/// 按钮点击音效。
///
/// 【这个脚本干什么？】
/// 给 UI 按钮加一个"按下就播放音效"的功能。
/// 挂到任何 Button 上，Inspector 里拖入一个音频文件，
/// 之后每次按这个按钮都会播放声音。
///
/// 【为什么不在 Button.onClick 里播？】
/// Button.onClick 是"点击"触发（按下+抬起，在同一按钮上才算点击）。
/// 本脚本用的是"按下"（PointerDown）触发，
/// 手感更灵敏——手指/鼠标一碰就响，不用等抬起。
///
/// 【为什么用 SoundManager 播？】
/// 如果直接用按钮上的 AudioSource 播：
///   - 按钮隐藏/禁用时音效会被打断
///   - 每个按钮要单独挂一个 AudioSource，浪费
/// SoundManager 是全局常驻的，用它播不受按钮状态影响。
///
/// 【使用示例】
/// 1. 选中一个 UI 按钮（比如开始游戏按钮）
/// 2. 把本脚本挂上去
/// 3. Inspector 里把 clickSound 拖一个音频文件
/// 4. 运行游戏 → 点按钮就有声音
/// </summary>
[RequireComponent(typeof(Button))]
// ↑ RequireComponent 是 Unity 的特性：
//   强制要求挂载本脚本的物体必须有 Button 组件。
//   因为本脚本是给按钮用的，没 Button 就无从"按"。
//   如果物体上没有 Button，Unity 会自动加一个。
public class ButtonClickSound : MonoBehaviour, IPointerDownHandler
//                       ↑ 实现接口     ↑ 定义在 EventSystems 里
// IPointerDownHandler 是 Unity 的"指针按下事件"接口，
// 只要一个 MonoBehaviour 实现了它，这个物体就会在"指针按下"时
// 收到 OnPointerDown 回调（前提是物体上有可接收射线的组件，比如 Button）。
{
    #region 公共字段

    [Header("音效")]
    [Tooltip("按钮按下时播放的音效，可在 Inspector 中指定。")]
    /// <summary>
    /// 按钮按下时播放的音效文件。
    /// 在 Inspector 里拖一个 AudioClip 进来。
    /// 如果留空，点击时就不会播放任何声音（但不会报错）。
    /// </summary>
    public AudioClip clickSound;

    #endregion

    #region 接口实现

    /// <summary>
    /// 按钮按下时触发，播放点击音效。
    ///
    /// 【谁调用这个方法？】
    /// Unity 的事件系统。
    /// 当用户点击 / 触摸屏幕，射线检测到本物体上的 Button 时，
    /// 事件系统会自动回调 OnPointerDown 方法。
    /// 
    /// 【参数 eventData 是什么？】
    /// 事件的详细信息（点击位置、按了哪个键、是不是触摸等）。
    /// 本脚本不需要用到，所以参数名保留但没读。
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        // 两个前提：
        //   1. 用户拖了音效文件
        //   2. SoundManager 已经初始化
        // 缺任何一个就直接返回，避免报错
        if (clickSound == null || SoundManager.Instance == null) return;

        // 通过全局 SoundManager 播放音效。
        // SoundManager.Instance 是单例，跨场景常驻，
        // 所以即使本按钮所在的 UI 面板被隐藏，
        // 音效也能正常播完（不像按钮上的 AudioSource 会被禁用）。
        SoundManager.Instance.PlaySound(clickSound);
    }

    #endregion
}