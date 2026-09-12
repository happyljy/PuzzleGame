using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 长按检测器。
/// 
/// 【功能】
/// 挂在任何 UI 元素上，实现"按住一段时间后触发"的效果。
/// 长按触发后，本次按下不会再触发 Button.onClick。
/// 
/// 【使用】
/// <code>
/// var handler = btnObj.AddComponent&lt;LongPressHandler&gt;();
/// handler.SetOnLongPress(() => { Debug.Log("长按触发"); });
/// 
/// btn.onClick.AddListener(() => {
///     if (LongPressHandler.ConsumeLongPressFlag()) return;   // 屏蔽长按后触发的点击
///     // 正常单击逻辑
/// });
/// </code>
/// </summary>
public class LongPressHandler : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    #region 公共字段

    [Header("长按设置")]
    [Tooltip("触发长按所需的按住时长（秒）")]
    public float longPressDuration = 0.8f;

    [Header("视觉反馈")]
    [Tooltip("长按过程中的缩放比例（1 表示不缩放）")]
    public float pressScale = 0.95f;

    [Tooltip("长按过程中的变暗颜色")]
    public Color pressTint = new Color(0.8f, 0.8f, 0.8f, 1f);

    #endregion

    #region 私有字段

    // 长按触发时的回调
    private Action _onLongPress;

    // 状态
    private bool _isPressing;              // 当前是否被按住
    private float _pressTimer;             // 按住累计时长
    private bool _longPressTriggered;      // 本次按下是否已触发长按（防重复触发）

    // 视觉缓存
    private Vector3 _originalScale;
    private Color _originalColor;
    private Image _image;

    /// <summary>
    /// 全局标记：本次按下过程中，是否已经触发过长按。
    /// 
    /// 【为什么用 static？】
    /// Button.onClick 无法直接访问这个脚本实例，
    /// 所以用一个静态标记让 Button 的 onClick 也能查询到。
    /// 
    /// 【为什么不用时间窗口？】
    /// 之前用"长按后 N 秒内忽略 onClick"的方案有漏洞：
    /// 如果用户长按后继续按很久才松手（超过 N 秒），
    /// onClick 会被正常触发，导致进入错误的面板。
    /// 改成"本次按下是否长按过"就完全可靠了。
    /// </summary>
    private static bool _longPressOccurredThisPress = false;

    #endregion

    #region 静态方法

    /// <summary>
    /// 消费"本次按下触发过长按"的标记。
    /// 
    /// - 如果本次按下触发过长按，返回 true 并**清除标记**；
    /// - 否则返回 false。
    /// 
    /// 用途：在 Button 的 onClick 里调用，屏蔽长按后触发的点击。
    /// </summary>
    public static bool ConsumeLongPressFlag()
    {
        if (_longPressOccurredThisPress)
        {
            _longPressOccurredThisPress = false;
            return true;
        }
        return false;
    }

    #endregion

    #region 公共方法

    /// <summary>设置长按触发时的回调。</summary>
    public void SetOnLongPress(Action callback)
    {
        _onLongPress = callback;
    }

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        _originalScale = transform.localScale;
        _image = GetComponent<Image>();
        if (_image != null) _originalColor = _image.color;
    }

    private void Update()
    {
        if (!_isPressing) return;

        _pressTimer += Time.unscaledDeltaTime;

        if (_pressTimer >= longPressDuration && !_longPressTriggered)
        {
            _longPressTriggered = true;
            _isPressing = false;

            // ★ 关键：记录"本次按下触发过长按"
            // 无论用户之后按多久才松手，onClick 都会被拦截
            _longPressOccurredThisPress = true;

            // 恢复视觉
            transform.localScale = _originalScale;
            if (_image != null) _image.color = _originalColor;

            // 触发回调
            _onLongPress?.Invoke();
        }
    }

    #endregion

    #region 事件接口

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressing = true;
        _pressTimer = 0f;
        _longPressTriggered = false;

        // ★ 重置标记：新的按下开始
        _longPressOccurredThisPress = false;

        // 视觉反馈：缩小 + 变暗
        transform.localScale = _originalScale * pressScale;
        if (_image != null) _image.color = pressTint;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        EndPress();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 手指滑出按钮范围，取消长按
        EndPress();
    }

    #endregion

    #region 私有方法

    private void EndPress()
    {
        _isPressing = false;

        // 恢复视觉
        transform.localScale = _originalScale;
        if (_image != null) _image.color = _originalColor;
    }

    #endregion
}