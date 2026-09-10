using UnityEngine;

/// <summary>
/// 安全区适配脚本：保持面板原有锚点，通过调整 offsetMin/offsetMax 适配刘海屏。
/// 适用于顶部固定、底部固定、全屏拉伸三种情况。
/// </summary>
public class SafeArea : MonoBehaviour
{
    public enum PanelType
    {
        Top,        // 顶部固定
        Bottom,     // 底部固定
        Stretch     // 全屏拉伸
    }

    public PanelType panelType = PanelType.Stretch;
    public float topOffset = 0f;      // 顶部额外留白（如固定高度）
    public float bottomOffset = 0f;   // 底部额外留白

    private RectTransform rectTransform;
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ApplySafeArea();
    }

    void Update()
    {
        if (Screen.safeArea != lastSafeArea)
            ApplySafeArea();
    }

    void ApplySafeArea()
    {
        Rect safeArea = Screen.safeArea;

        // 计算安全区的像素边界
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
}