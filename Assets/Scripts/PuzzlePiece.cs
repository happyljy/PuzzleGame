using UnityEngine;                 // Unity 基础 API
using UnityEngine.EventSystems;    // 事件系统（IPointerDownHandler、IDragHandler 等）
using UnityEngine.UI;              // UI 组件（Image、Button）

/// <summary>
/// 拼图碎片脚本。
///
/// 【挂在哪种物体上？】
/// 挂在 piecePrefab（碎片预制体）上。
/// 每个碎片都有这个脚本，各自独立处理自己的交互。
///
/// 【两种状态】
///   1. 列表中（interactable = false）
///      - 只显示，不可拖拽、不可旋转
///      - 点击会把它移到拼图区（由 GameManager 处理）
///   2. 拼图区中（interactable = true）
///      - 可以拖拽、可以点击旋转
///      - 位置正确且角度为 0 时自动锁定
///
/// 【实现的接口】
/// 这些接口由 Unity 的事件系统自动调用，
/// 只要挂载脚本的物体上有 Graphic（Image）并且 Raycast Target = true，
/// 触摸/鼠标操作时就会回调对应的方法。
///
///   IPointerDownHandler  → OnPointerDown   （按下）
///   IPointerUpHandler    → OnPointerUp     （抬起）
///   IBeginDragHandler    → OnBeginDrag     （开始拖拽）
///   IDragHandler         → OnDrag          （拖拽中）
///   IEndDragHandler      → OnEndDrag       （结束拖拽）
/// </summary>
public class PuzzlePiece : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler,
    IDragHandler, IBeginDragHandler, IEndDragHandler
{
    #region 常量

    /// <summary>
    /// 点击与拖拽的移动距离阈值（像素）。
    /// 按下到抬起如果移动距离小于这个值，视为"点击"（触发旋转）；
    /// 大于这个值，视为"拖拽"（不触发旋转）。
    /// 
    /// 【为什么要这个阈值？】
    /// 手指触摸时难免有轻微移动。如果完全没有阈值，
    /// 用户想"点击旋转"时稍微滑一下就变成拖拽了。
    /// 10 像素能容忍轻微抖动。
    /// </summary>
    private const float ClickThreshold = 10f;

    /// <summary>
    /// 吸附距离阈值（像素）。
    /// 碎片距离目标位置小于这个值，且旋转角度为 0 时自动锁定。
    /// 
    /// 【阈值多大合适？】
    /// - 太小（比如 5）：玩家很难对准，体验差
    /// - 太大（比如 100）：容易误锁，感觉"强行帮我放"
    /// 30 左右比较合适。
    /// </summary>
    private const float SnapDistance = 30f;

    #endregion

    #region 公共字段

    // [HideInInspector] 表示这个字段不在 Inspector 面板显示。
    // 因为它们的值由 GameManager 在运行时设置，不需要手动拖。

    [HideInInspector] public int pieceIndex;        // 碎片在完整图中的原始索引（从左到右、从上到下）
    [HideInInspector] public int currentRotation;   // 当前旋转角度（0/90/180/270）
    [HideInInspector] public bool isLocked;         // 是否已锁定（正确放置）
    [HideInInspector] public Vector2 targetPosition;// 正确的目标位置（拼图区域本地坐标）

    /// <summary>
    /// 是否可交互。
    /// - 列表中：false（只显示，点击后由 GameManager 处理）
    /// - 拼图区：true（可拖拽、可旋转）
    /// 
    /// 注意这个是 public 且不在 HideInInspector 里，
    /// 因为 GameManager 需要在创建时直接赋值。
    /// </summary>
    public bool interactable = true;

    [Header("音效")]
    /// <summary>
    /// 碎片锁定时的音效。
    /// 可以每个碎片用同一个，也可以不同碎片不同音效。
    /// 直接在 Inspector 拖一个 AudioClip。
    /// </summary>
    public AudioClip lockSound;

    #endregion

    #region 私有字段

    /// <summary>
    /// 缓存的 RectTransform。
    /// 
    /// 【为什么要缓存？】
    /// GetComponent<RectTransform>() 每次调用都有性能开销。
    /// 在 Awake 里拿一次，后续直接用字段，省性能。
    /// </summary>
    private RectTransform rectTransform;

    /// <summary>
    /// 按下时的指针位置。
    /// 用于判断"按下 → 抬起"过程中移动了多少像素，
    /// 从而区分"点击"和"拖拽"。
    /// </summary>
    private Vector2 pointerDownPosition;

    /// <summary>
    /// 是否正在拖拽中。
    /// 在 OnBeginDrag 里设为 true，OnEndDrag 里设为 false。
    /// 用途：抬起时如果 isDragging 还是 false，说明没拖拽过 → 视为点击。
    /// </summary>
    private bool isDragging;

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake 在物体创建时立即调用。
    /// 这里缓存 RectTransform。
    /// </summary>
    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化碎片的外观和状态。
    /// 由 GameManager 在创建碎片时调用。
    /// </summary>
    /// <param name="sprite">碎片显示的图片（来自切割原图）</param>
    /// <param name="index">碎片编号（在完整图中的原始位置）</param>
    /// <param name="rotation">初始旋转角度（0/90/180/270）</param>
    public void Initialize(Sprite sprite, int index, int rotation)
    {
        GetComponent<Image>().sprite = sprite;                          // 设置图片
        pieceIndex = index;                                             // 记录编号
        currentRotation = rotation;                                     // 记录旋转角
        rectTransform.localRotation = Quaternion.Euler(0, 0, rotation); // 应用旋转
        isLocked = false;                                               // 未锁定
        GetComponent<Image>().color = Color.white;                      // 恢复白色
    }

    #endregion

    #region 拖拽接口实现

    /// <summary>
    /// 按下时触发（Unity 事件系统自动调用）。
    /// 这里记录按下位置，重置拖拽标志。
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        // 不可交互或已锁定 → 忽略
        if (!interactable || isLocked) return;

        // eventData.position 是屏幕坐标
        pointerDownPosition = eventData.position;

        // 重置拖拽标志（新的一次按下开始）
        isDragging = false;
    }

    /// <summary>
    /// 开始拖拽时触发。
    /// </summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;

        // ---------- 多点触摸保护 ----------
        // 如果玩家正在用双指缩放，就不应该拖碎片。
        // 这里通过 GameManager.IsMultiTouch 和 Input.touchCount 双重判断。
        if (GameManager.Instance != null && GameManager.Instance.IsMultiTouch) return;
        if (Input.touchCount >= 2) return;

        isDragging = true;

        // 把碎片移到父容器的最后一个子物体位置（即"最上层"）
        // 这样拖拽中的碎片会显示在其他碎片之上，不会被遮挡
        transform.SetAsLastSibling();
    }

    /// <summary>
    /// 拖拽过程中每帧触发。
    /// 让碎片跟随指针移动，并限制在拼图区域内。
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        if (GameManager.Instance != null && GameManager.Instance.IsMultiTouch) return;
        if (Input.touchCount >= 2) return;   // 双指时立即退出

        RectTransform puzzleArea = GameManager.Instance.puzzleArea;
        if (puzzleArea == null) return;

        // ---------- 获取拼图区域的"世界坐标"四角 ----------
        // GetWorldCorners 返回 4 个角，顺序是：
        //   [0] 左下
        //   [1] 左上
        //   [2] 右上
        //   [3] 右下
        Vector3[] corners = new Vector3[4];
        puzzleArea.GetWorldCorners(corners);
        float minX = corners[0].x;   // 左边界
        float maxX = corners[2].x;   // 右边界
        float minY = corners[0].y;   // 下边界
        float maxY = corners[2].y;   // 上边界

        // ---------- 计算碎片的半宽半高 ----------
        // 因为要做边界限制，碎片不能超过边界，需要留出半个碎片的尺寸。
        // 
        // 注意：这里乘了 parentScale，因为拼图内容容器可能被缩放
        // （双指缩放时 puzzleContent.localScale 会变），
        // 碎片的实际显示尺寸 = 自身尺寸 × 父物体缩放。
        Vector2 pieceSize = rectTransform.sizeDelta;
        Vector3 parentScale = rectTransform.parent.localScale;
        float halfWidth = pieceSize.x * parentScale.x * 0.5f;
        float halfHeight = pieceSize.y * parentScale.y * 0.5f;

        // ---------- 把屏幕坐标转换为世界坐标 ----------
        // ScreenPointToWorldPointInRectangle 会考虑相机的投影，
        // 把屏幕点正确映射到 UI 平面上的世界坐标。
        Vector3 worldPoint;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            puzzleArea, eventData.position, eventData.pressEventCamera, out worldPoint);

        // ---------- 边界限制（Clamp） ----------
        // Mathf.Clamp(value, min, max) 把 value 限制在 [min, max]。
        // 这里保证碎片完整地在拼图区域内，不会有一半超出边界。
        worldPoint.x = Mathf.Clamp(worldPoint.x, minX + halfWidth, maxX - halfWidth);
        worldPoint.y = Mathf.Clamp(worldPoint.y, minY + halfHeight, maxY - halfHeight);

        // ---------- 世界坐标 → 父物体本地坐标 ----------
        // 因为 anchoredPosition 是"相对于父物体的锚点"，
        // 所以要把世界坐标反算成父物体的本地坐标。
        Vector3 localPos = rectTransform.parent.InverseTransformPoint(worldPoint);
        rectTransform.localPosition = localPos;
    }

    /// <summary>
    /// 结束拖拽时触发。
    /// 这里检查是否应该吸附锁定。
    /// </summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;

        // ---------- 吸附判断 ----------
        // 两个条件同时满足才锁定：
        //   1. 距离目标位置 < SnapDistance 像素
        //   2. 旋转角度 = 0（方向正确）
        if (Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < SnapDistance
            && currentRotation == 0)
        {
            LockPiece();
        }

        isDragging = false;
    }

    /// <summary>
    /// 指针抬起时触发。
    /// 这里判断是"点击"还是"拖拽"，点击则旋转。
    /// </summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;

        // ---------- 点击判断 ----------
        // 两个条件同时满足才视为点击：
        //   1. 没有处于拖拽状态（isDragging = false）
        //   2. 按下到抬起移动距离 < ClickThreshold
        if (!isDragging && Vector2.Distance(pointerDownPosition, eventData.position) < ClickThreshold)
        {
            RotatePiece();
        }
    }

    #endregion

    #region 旋转与锁定

    /// <summary>
    /// 顺时针旋转 90°。
    /// 
    /// 【旋转角度循环】
    /// 0 → 90 → 180 → 270 → 0 → ...
    /// 用 (currentRotation + 90) % 360 实现循环。
    /// 
    /// 【旋转后自动锁定】
    /// 如果旋转回 0° 时位置又正好正确，立即锁定。
    /// 这比"必须松手才能锁定"更友好。
    /// </summary>
    private void RotatePiece()
    {
        // 累加 90 度并取模
        currentRotation = (currentRotation + 90) % 360;

        // 应用到 Transform
        rectTransform.localRotation = Quaternion.Euler(0, 0, currentRotation);

        // 旋转回 0° + 位置正确 → 立即锁定
        if (currentRotation == 0
            && Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < SnapDistance)
        {
            LockPiece();
        }
    }

    /// <summary>
    /// 锁定碎片。
    /// 
    /// 【锁定 = 以下所有条件满足】
    ///   1. 位置精确对齐目标位置
    ///   2. 旋转归零
    ///   3. 颜色变粉（视觉反馈）
    ///   4. 不再响应任何交互
    ///   5. 通知 GameManager 检查是否胜利
    /// </summary>
    private void LockPiece()
    {
        // ---------- 状态更新 ----------
        isLocked = true;

        // 精确对齐到目标位置（避免 SnapDistance 范围内的小偏移）
        rectTransform.anchoredPosition = targetPosition;
        rectTransform.localRotation = Quaternion.identity;   // 归零旋转
        currentRotation = 0;

        // 视觉反馈：淡粉色（与未锁定的白色区分）
        GetComponent<Image>().color = new Color(1f, 0.8f, 0.8f, 1f);

        // ---------- 播放音效 ----------
        // 最后一块不播锁定音效，因为马上要播胜利音效，两个叠一起会吵
        if (GameManager.Instance.LockedCount < GameManager.Instance.TotalPieces - 1)
        {
            if (lockSound != null && SoundManager.Instance != null)
                SoundManager.Instance.PlayPuzzleSound(lockSound);
        }

        // ---------- 通知 GameManager ----------
        // 它会累加 lockedCount，判断是否全部完成
        GameManager.Instance.CheckVictory();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 强制把碎片位置限制在拼图区内。
    /// 
    /// 【使用时机】
    /// 列表里的碎片被点击进入拼图区时，GameManager 会给它一个随机初始位置。
    /// 随机位置可能在边界外，所以调用这个方法把它拉回边界内。
    /// 
    /// 【与 OnDrag 里的 Clamp 有什么区别？】
    /// - OnDrag 里的 Clamp 是"每帧"处理，用世界坐标
    /// - 这里是一次性的，用世界坐标直接赋值
    /// 本质上做的是同一件事。
    /// </summary>
    public void ClampPositionToPuzzleArea()
    {
        RectTransform puzzleArea = GameManager.Instance.puzzleArea;
        if (puzzleArea == null) return;

        // 获取拼图区四角
        Vector3[] corners = new Vector3[4];
        puzzleArea.GetWorldCorners(corners);
        float minX = corners[0].x;
        float maxX = corners[2].x;
        float minY = corners[0].y;
        float maxY = corners[2].y;

        // 碎片半宽半高
        Vector2 pieceSize = rectTransform.sizeDelta;
        Vector3 parentScale = rectTransform.parent.localScale;
        float halfWidth = pieceSize.x * parentScale.x * 0.5f;
        float halfHeight = pieceSize.y * parentScale.y * 0.5f;

        // 当前世界坐标
        Vector3 currentWorldPos = rectTransform.position;

        // 限制范围
        currentWorldPos.x = Mathf.Clamp(currentWorldPos.x, minX + halfWidth, maxX - halfWidth);
        currentWorldPos.y = Mathf.Clamp(currentWorldPos.y, minY + halfHeight, maxY - halfHeight);

        // 直接设置世界坐标。
        // Unity 会自动把它换算成父物体下的本地坐标，
        // 所以不需要手动算 localPosition。
        rectTransform.position = currentWorldPos;
    }

    #endregion
}