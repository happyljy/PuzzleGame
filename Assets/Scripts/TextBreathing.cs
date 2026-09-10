using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 文字呼吸效果：让 Text 组件的透明度在 minAlpha 和 maxAlpha 之间
/// 以正弦波形式循环变化，形成“呼吸”般的闪烁效果。
/// 常用于加载提示、提示文字等。
/// </summary>
[RequireComponent(typeof(Text))]
public class TextBreathing : MonoBehaviour
{
    #region 公共字段

    [Header("呼吸参数")]
    [Tooltip("呼吸速度（数值越大，闪烁越快）")]
    public float speed = 2f;

    [Tooltip("最小透明度（0~1）")]
    [Range(0f, 1f)]
    public float minAlpha = 0.3f;

    [Tooltip("最大透明度（0~1）")]
    [Range(0f, 1f)]
    public float maxAlpha = 1f;

    #endregion

    #region 私有字段

    private Text text;              // 缓存的 Text 组件
    private Color originalColor;    // 原始颜色（保留 RGB，只修改 A）

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        text = GetComponent<Text>();
        if (text != null)
            originalColor = text.color;
        else
            Debug.LogWarning("TextBreathing: 未找到 Text 组件，脚本将无效。");
    }

    private void Update()
    {
        if (text == null) return;

        // 使用正弦波在 [0,1] 之间振荡：sin 范围 [-1,1] → (sin+1)/2 范围 [0,1]
        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;

        // 将 t 映射到 [minAlpha, maxAlpha]
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);

        // 应用透明度，保留原有 RGB
        text.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
    }

    #endregion
}