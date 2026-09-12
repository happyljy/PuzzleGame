using UnityEngine;         // Unity 基础 API
using UnityEngine.UI;      // UI 组件（Text）

/// <summary>
/// 文字呼吸效果。
///
/// 【这个脚本干什么？】
/// 让一个 Text 组件的透明度循环变化，
/// 从"半透明 → 完全显示 → 半透明 → 完全显示……"
/// 看起来像在"呼吸"或"闪烁"。
///
/// 【典型用途】
/// - 加载界面"加载中..."的提示文字
/// - "点击屏幕继续"的提示
/// - 按钮文字强调
/// - 打字机效果前的心跳提示
///
/// 【效果演示】
/// 透明度变化曲线（正弦波）：
///   alpha
///   1.0 ┤     ╭─╮         ╭─╮
///       │    ╱   ╲       ╱   ╲
///   0.6 ┤   ╱     ╲     ╱     ╲
///       │  ╱       ╲   ╱       ╲
///   0.3 ┤ ╱         ╲ ╱         ╲
///       │╱           V           V
///       └──────────────────────────→ 时间
///
/// 【核心原理】
/// 用 sin 函数产生循环的"波浪"，
/// 把 [-1, 1] 的取值映射到 [minAlpha, maxAlpha]，
/// 每一帧更新 alpha。
/// </summary>
[RequireComponent(typeof(Text))]
// ↑ Unity 特性：强制要求挂载本脚本的物体必须有 Text 组件。
//   因为本脚本只对 Text 有意义，没有 Text 就没法"呼吸"。
//   如果物体上没有 Text，Unity 会自动加一个。
public class TextBreathing : MonoBehaviour
{
    #region 公共字段（Inspector 面板可调）

    [Header("呼吸参数")]
    [Tooltip("呼吸速度（数值越大，闪烁越快）")]
    /// <summary>
    /// 呼吸速度。
    /// 
    /// 【数值含义】
    /// - 1  → 大约 6.28 秒一个完整周期（慢）
    /// - 2  → 大约 3.14 秒一个完整周期（中）
    /// - 4  → 大约 1.57 秒一个完整周期（快）
    /// 
    /// 【数学原理】
    /// 公式里是 sin(Time.time * speed)，
    /// sin 的完整周期是 2π ≈ 6.28，
    /// 所以周期 = 2π / speed 秒。
    /// </summary>
    public float speed = 2f;

    [Tooltip("最小透明度（0~1）")]
    [Range(0f, 1f)]
    // ↑ Range 特性：在 Inspector 里显示成一个滑动条，范围 0~1。
    //   防止开发者/美术手滑填个 1.5 或 -0.3 之类的非法值。
    /// <summary>
    /// 最暗时的透明度。
    /// 0 = 完全透明（看不见），1 = 完全不透明。
    /// 一般 0.2 ~ 0.5 比较合适，太透明会看不到文字。
    /// </summary>
    public float minAlpha = 0.3f;

    [Tooltip("最大透明度（0~1）")]
    [Range(0f, 1f)]
    /// <summary>
    /// 最亮时的透明度。
    /// 一般设为 1（完全显示），也可以设 0.8 让文字看起来不那么刺眼。
    /// </summary>
    public float maxAlpha = 1f;

    #endregion

    #region 私有字段

    /// <summary>
    /// 缓存的 Text 组件。
    /// 
    /// 【为什么要缓存？】
    /// GetComponent<Text>() 每次调用都有一定开销，
    /// 在 Update 里每帧调用会浪费性能。
    /// 在 Start 里调一次缓存下来，后续直接读字段。
    /// </summary>
    private Text text;

    /// <summary>
    /// 原始颜色。
    /// 
    /// 【为什么要保存？】
    /// 美术在 Inspector 里可能把文字设成了金色、青色等。
    /// 我们在改透明度时，如果直接 text.color = Color.red，
    /// 就会覆盖掉原来的颜色。
    /// 
    /// 正确做法：
    ///   1. 保存原始颜色（含 RGB）
    ///   2. 每帧只改 alpha 通道
    ///   3. RGB 用保存的原值
    /// </summary>
    private Color originalColor;

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Start 在物体第一帧启用时调用。
    /// 初始化：缓存 Text 和原始颜色。
    /// </summary>
    private void Start()
    {
        // 获取 Text 组件
        // [RequireComponent] 已经保证了它一定存在，
        // 但保险起见还是判一下空。
        text = GetComponent<Text>();

        if (text != null)
        {
            // 保存原始颜色（后续每帧都要用到它来取 RGB）
            originalColor = text.color;
        }
        else
        {
            // 理论上不会走到这里（RequireComponent 保证有 Text），
            // 但万一是通过 AddComponent 手动加的，还是给个警告
            Debug.LogWarning("TextBreathing: 未找到 Text 组件，脚本将无效。");
        }
    }

    /// <summary>
    /// Update 每帧调用。
    /// 计算当前透明度并应用到 Text。
    /// </summary>
    private void Update()
    {
        // 组件不存在就直接返回
        if (text == null) return;

        // ============ 第 1 步：用 sin 生成波浪值 ============
        //
        // 【sin 函数复习】
        // y = sin(x) 的值域是 [-1, 1]
        //   x = 0        → y = 0
        //   x = π/2      → y = 1      （最大值）
        //   x = π        → y = 0
        //   x = 3π/2     → y = -1     （最小值）
        //   x = 2π       → y = 0      （循环）
        //
        // 【Time.time 是什么？】
        // 从游戏开始到现在经过的秒数（浮点数，每帧都在增长）。
        //
        // 【为什么乘 speed？】
        // Time.time 增长速度固定（每秒 +1），
        // 乘上 speed 相当于"加速时间"：
        //   speed = 1 → 每秒正弦波前进 1 弧度
        //   speed = 2 → 每秒前进 2 弧度（周期缩短一半）
        // 效果就是 speed 越大，呼吸越快。
        //
        // 【为什么 +1 再乘 0.5？】
        // sin 的范围是 [-1, 1]，
        // +1 变成 [0, 2]，
        // 乘 0.5 变成 [0, 1]。
        // 这样 t 就是一个标准的 0~1 进度值，方便后面插值。
        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;

        // ============ 第 2 步：映射到 alpha 范围 ============
        //
        // 【Lerp 复习】
        // Mathf.Lerp(a, b, t)
        //   t = 0 → 返回 a
        //   t = 1 → 返回 b
        //   t = 0.5 → 返回 (a+b)/2
        //
        // 这里 t 在 [0, 1] 之间循环，
        // Lerp 就返回 minAlpha 到 maxAlpha 之间的值。
        //   t = 0 → minAlpha（最暗）
        //   t = 1 → maxAlpha（最亮）
        //   t = 0.5 → 中间值
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);

        // ============ 第 3 步：应用颜色 ============
        //
        // 用 new Color(r, g, b, a) 构造新颜色：
        //   r/g/b 用保存的原始值（保持美术设的颜色）
        //   a 用刚才计算出来的（每帧变化）
        text.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
    }

    #endregion
}