using UnityEngine;

/// <summary>
/// 安全区适配脚本。
///
/// 【什么是"安全区"？】
/// 现在的手机屏幕五花八门：
///   - iPhone 有"刘海"（屏幕顶部一块黑色）
///   - 安卓有"挖孔屏"（摄像头在屏幕中间挖个洞）
///   - 还有"曲面屏""水滴屏"等等
/// 如果 UI 直接铺满整个屏幕，重要的按钮可能被刘海挡住或挖孔遮住。
///
/// 手机的"安全区"（Safe Area）就是：
///   "把刘海、圆角、Home 条都避开之后，真正安全可以用来显示 UI 的矩形区域"
///
/// 【本脚本做什么？】
/// 保持面板原有的锚点（anchor）不变，
/// 通过调整 offsetMin / offsetMax 这两个"边距值"，
/// 把面板自动缩小到安全区内，让 UI 不被遮挡。
///
/// 【支持的三种面板类型】
/// - Top     ：顶部固定栏（如标题栏），下移避开刘海
/// - Bottom  ：底部固定栏（如 Tab 栏），上移避开 Home 条
/// - Stretch ：全屏拉伸背景，四边都缩到安全区
///
/// 【使用说明】
/// - 顶部面板：PanelType = Top，锚点 (0,1)~(1,1)，Pivot (0.5,1)
/// - 底部面板：PanelType = Bottom，锚点 (0,0)~(1,0)，Pivot (0.5,0)
/// - 全屏面板：PanelType = Stretch，锚点 (0,0)~(1,1)
/// </summary>
[RequireComponent(typeof(RectTransform))]
// ↑ RequireComponent 是 Unity 的一个特性：
//   它会强制要求"挂载本脚本的物体必须有 RectTransform 组件"。
//   如果没有，Unity 会自动帮它加一个。
//   因为 SafeArea 只对 UI 元素有意义，必须有 RectTransform 才能改 offset。
public class SafeArea : MonoBehaviour
{
    #region 枚举

    /// <summary>
    /// 面板类型，决定安全区适配方式。
    /// </summary>
    public enum PanelType
    {
        /// <summary>顶部固定（例如标题栏）——只调整顶部边界</summary>
        Top,
        /// <summary>底部固定（例如 Tab 栏）——只调整底部边界</summary>
        Bottom,
        /// <summary>全屏拉伸（例如背景图）——四边都调整</summary>
        Stretch
    }

    #endregion

    #region 公共字段（Inspector 面板可调）

    [Header("适配设置")]
    [Tooltip("面板类型：Top / Bottom / Stretch")]
    /// <summary>
    /// 面板类型。
    /// 决定用哪种方式应用安全区（只看顶部 / 只看底部 / 四边都看）。
    /// 默认 Stretch（全屏）。
    /// </summary>
    public PanelType panelType = PanelType.Stretch;

    [Tooltip("顶部额外留白（像素），用于给固定高度的顶部栏留出空间")]
    /// <summary>
    /// 顶部额外留白。
    /// 例如你的 UI 上方有一个 60 像素高的状态栏，就设 topOffset = 60，
    /// 让 SafeArea 额外往下挪 60 像素，避免面板被状态栏压住。
    /// 大部分情况留 0 即可。
    /// </summary>
    public float topOffset = 0f;

    [Tooltip("底部额外留白（像素），用于给固定高度的底部栏留出空间")]
    /// <summary>
    /// 底部额外留白。
    /// 同上，用于给底部固定的导航栏/Home 条留出空间。
    /// </summary>
    public float bottomOffset = 0f;

    #endregion

    #region 私有字段

    /// <summary>
    /// 缓存自身的 RectTransform 组件。
    /// 为什么不每次用 GetComponent 拿？
    /// 因为 GetComponent 有一定开销，缓存下来能省性能。
    /// </summary>
    private RectTransform rectTransform;

    /// <summary>
    /// 上一次记录的安全区。
    /// 用于和当前帧对比，如果没变化就不重复调整（省性能）。
    /// 初始值设为 (0,0,0,0) 这种不可能的值，保证第一帧一定会应用一次。
    /// </summary>
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake 在物体被创建时立即调用（早于 Start）。
    /// 这里做初始化：拿组件 + 应用一次安全区。
    /// </summary>
    private void Awake()
    {
        // 拿到自己的 RectTransform 组件（UI 元素必须有的组件）
        rectTransform = GetComponent<RectTransform>();

        // 立刻应用一次安全区（避免开始时显示错位）
        ApplySafeArea();
    }

    /// <summary>
    /// Update 每帧调用。
    /// 检测屏幕安全区有没有变化，变了就重新适配。
    ///
    /// 【什么时候会变？】
    /// - 用户旋转手机（横屏↔竖屏）
    /// - 从分屏模式切回全屏
    /// - 展开/收起系统通知栏
    /// - 从后台切回前台（某些系统会重新计算安全区）
    /// </summary>
    private void Update()
    {
        // 每帧对比一次，只有变化时才重新计算，省性能
        if (Screen.safeArea != lastSafeArea)
            ApplySafeArea();
    }

    #endregion

    #region 安全区应用

    /// <summary>
    /// 根据当前屏幕安全区和面板类型，调整 RectTransform 的偏移。
    ///
    /// 【核心概念：UI 坐标系统】
    /// Unity 的 UI 有三个关键概念：
    ///   - Anchor（锚点）：UI 相对于父物体的"参考点"
    ///   - Pivot（轴心）：UI 自身的"旋转/缩放中心"
    ///   - offsetMin/offsetMax：UI 四边相对于锚点的"边距"
    ///
    /// 当 anchorMin == anchorMax（比如 Top 类型）时：
    ///   - offsetMin 控制"左下相对锚点的偏移"
    ///   - offsetMax 控制"右上相对锚点的偏移"
    ///
    /// 当 anchorMin != anchorMax（比如 Stretch 类型）时：
    ///   - offsetMin 是"左边界和下边界"相对锚点的偏移
    ///   - offsetMax 是"右边界和上边界"相对锚点的偏移
    ///   （注意：offsetMax 的两个分量都是负数表示向内缩）
    /// </summary>
    private void ApplySafeArea()
    {
        if (rectTransform == null) return;

        // 拿到当前屏幕的安全区（单位：像素）
        // Rect 的四个字段：
        //   x      = 安全区左下角 x 坐标
        //   y      = 安全区左下角 y 坐标（坐标系原点在"屏幕左下角"）
        //   width  = 安全区宽度
        //   height = 安全区高度
        Rect safeArea = Screen.safeArea;

        // ================= 计算顶部/底部的"被挡住多少像素" =================
        //
        // 【坐标系差异】
        // Screen 坐标系：原点在"屏幕左下角"，Y 轴向上
        // 但锚点坐标（锚定顶部时）：原点在"屏幕顶部"，Y 轴向下
        // 所以要把它们换算一下。
        //
        // 屏幕总高度 = Screen.height
        // 安全区顶部到屏幕顶部的距离 = Screen.height - (safeArea.y + safeArea.height)
        // 即：safeTop = 屏幕高 - (安全区下边 + 安全区高)
        //          = 屏幕高 - 安全区上边
        // 例：屏幕高 2400，safeArea.y=100，safeArea.height=2200
        //      → 安全区上边 = 100+2200 = 2300
        //      → safeTop = 2400 - 2300 = 100（顶部被刘海挡了 100 像素）
        float safeTop = Screen.height - (safeArea.y + safeArea.height);

        // 底部被挡住的距离就是 safeArea.y 本身
        // 因为 Screen 坐标系原点在屏幕左下角，
        // safeArea.y 就是"安全区下边到屏幕下边的距离"。
        float safeBottom = safeArea.y;

        switch (panelType)
        {
            case PanelType.Top:
                // ---------- 顶部固定面板 ----------
                // 保持 anchorMin=(0,1), anchorMax=(1,1), pivot=(0.5,1) 不变。
                // 只调整 offsetMax.y，让整个面板向下挪 safeTop 像素。
                //
                // offsetMax.y 的语义是"上边界相对锚点的偏移"。
                // 因为锚点(0,1)在屏幕顶部，所以：
                //   - 正值 → 上边界超过顶部（跑出屏幕）
                //   - 负值 → 上边界下移
                // 要避开刘海，就让上边界下移 safeTop，即 offsetMax.y = -safeTop。
                rectTransform.offsetMax = new Vector2(0, -safeTop - topOffset);
                // 加了 topOffset 是"额外的留白"，用于给状态栏等再留点位置。
                break;

            case PanelType.Bottom:
                // ---------- 底部固定面板 ----------
                // 保持 anchorMin=(0,0), anchorMax=(1,0), pivot=(0.5,0) 不变。
                // 只调整 offsetMin.y，让整个面板向上挪 safeBottom 像素。
                //
                // offsetMin.y 的语义是"下边界相对锚点的偏移"。
                // 因为锚点(0,0)在屏幕底部，所以：
                //   - 正值 → 下边界上移
                //   - 负值 → 下边界超出底部
                // 要避开 Home 条，就让下边界上移 safeBottom。
                rectTransform.offsetMin = new Vector2(0, safeBottom + bottomOffset);
                break;

            case PanelType.Stretch:
                // ---------- 全屏拉伸面板 ----------
                // 保持 anchorMin=(0,0), anchorMax=(1,1) 不变。
                // 四边都要调整。
                //
                // offsetMin：左下边距
                //   - x = 0（左右不动）
                //   - y = safeBottom + bottomOffset（下边界上移）
                //
                // offsetMax：右上边距
                //   - x = 0（左右不动）
                //   - y = -safeTop - topOffset（上边界下移）
                rectTransform.offsetMin = new Vector2(0, safeBottom + bottomOffset);
                rectTransform.offsetMax = new Vector2(0, -safeTop - topOffset);
                break;
        }

        // 记录本次安全区，供 Update 对比用
        lastSafeArea = safeArea;
    }

    #endregion
}