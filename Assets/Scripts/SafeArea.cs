using UnityEngine;

/// <summary>
/// 安全区适配脚本：保持面板原有锚点，通过调整 offsetMin/offsetMax 适配刘海屏。
/// 适用于顶部固定、底部固定、全屏拉伸三种情况。
/// 
/// 使用说明：
/// - 顶部面板：PanelType = Top，保持原有锚点（Anchor Min=(0,1), Max=(1,1), Pivot=(0.5,1)）。
/// - 底部面板：PanelType = Bottom，保持原有锚点（Anchor Min=(0,0), Max=(1,0), Pivot=(0.5,0)）。
/// - 全屏面板：PanelType = Stretch，保持原有锚点（Anchor Min=(0,0), Max=(1,1)）。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    #region 枚举

    /// <summary>面板类型，决定安全区适配方式。</summary>
    public enum PanelType
    {
        Top,        // 顶部固定
        Bottom,     // 底部固定
        Stretch     // 全屏拉伸
    }

    #endregion

    #region 公共字段

    [Header("适配设置")]
    [Tooltip("面板类型：Top / Bottom / Stretch")]
    public PanelType panelType = PanelType.Stretch;

    [Tooltip("顶部额外留白（像素），用于给固定高度的顶部栏留出空间")]
    public float topOffset = 0f;

    [Tooltip("底部额外留白（像素），用于给固定高度的底部栏留出空间")]
    public float bottomOffset = 0f;

    #endregion

    #region 私有字段

    private RectTransform rectTransform;            // 缓存的 RectTransform
    private Rect lastSafeArea = new Rect(0, 0, 0, 0); // 上一次的安全区，用于变化检测

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ApplySafeArea();
    }

    private void Update()
    {
        // 安全区变化时重新应用（例如旋转屏幕、切换应用）
        if (Screen.safeArea != lastSafeArea)
            ApplySafeArea();
    }

    #endregion

    #region 安全区应用

    /// <summary>
    /// 根据当前屏幕安全区和面板类型，调整 RectTransform 的偏移。
    /// </summary>
    private void ApplySafeArea()
    {
        if (rectTransform == null) return;

        Rect safeArea = Screen.safeArea;

        // 计算安全区的像素边界（注意 Screen 坐标系 Y 轴向上，而锚点坐标系可能相反）
        float safeTop = Screen.height - (safeArea.y + safeArea.height);
        float safeBottom = safeArea.y;

        switch (panelType)
        {
            case PanelType.Top:
                // 顶部固定：保持 anchorMin=(0,1), anchorMax=(1,1), pivot=(0.5,1)
                // 把面板顶部下移 safeTop 像素
                rectTransform.offsetMax = new Vector2(0, -safeTop - topOffset);
                break;

            case PanelType.Bottom:
                // 底部固定：保持 anchorMin=(0,0), anchorMax=(1,0), pivot=(0.5,0)
                // 把面板底部上移 safeBottom 像素
                rectTransform.offsetMin = new Vector2(0, safeBottom + bottomOffset);
                break;

            case PanelType.Stretch:
                // 全屏拉伸：四边都考虑安全区
                rectTransform.offsetMin = new Vector2(0, safeBottom + bottomOffset);
                rectTransform.offsetMax = new Vector2(0, -safeTop - topOffset);
                break;
        }

        lastSafeArea = safeArea;
    }

    #endregion
}