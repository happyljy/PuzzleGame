using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 拼图碎片脚本：负责单个碎片的交互，包括点击旋转、拖拽移动、
/// 边界限制、自动吸附锁定，以及通知 GameManager 检查胜利。
/// 同时支持列表中的碎片（不可交互）和拼图区域中的碎片（可交互）。
/// </summary>
public class PuzzlePiece : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    // ==================== 公共字段 ====================

    [HideInInspector] public int pieceIndex;       // 碎片在完整图中的原始索引（从左到右、从上到下）
    [HideInInspector] public int currentRotation;  // 当前旋转角度（0/90/180/270）
    [HideInInspector] public bool isLocked = false; // 是否已锁定（正确放置）
    [HideInInspector] public Vector2 targetPosition; // 正确的目标位置（拼图区域本地坐标）
    public bool interactable = true;               // 是否可交互（列表中为 false，拼图区域中为 true）

    // ==================== 私有字段 ====================

    private RectTransform rectTransform;           // 碎片的 RectTransform
    private Vector2 pointerDownPosition;           // 按下时指针位置（用于区分点击和拖拽）
    private bool isDragging = false;               // 是否正在拖拽
    private const float clickThreshold = 10f;      // 点击与拖拽的移动距离阈值（像素）
    private const float snapDistance = 30f;        // 吸附距离阈值（像素）

    // ==================== 初始化 ====================

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    /// <summary>
    /// 初始化碎片的外观和状态
    /// </summary>
    /// <param name="sprite">碎片显示的图片</param>
    /// <param name="index">碎片编号</param>
    /// <param name="rotation">初始旋转角度</param>
    public void Initialize(Sprite sprite, int index, int rotation)
    {
        GetComponent<Image>().sprite = sprite;
        pieceIndex = index;
        currentRotation = rotation;
        rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
        isLocked = false;
        GetComponent<Image>().color = Color.white; // 恢复为白色
    }

    // ==================== 拖拽接口 ====================

    /// <summary>
    /// 按下时记录指针位置，重置拖拽标志
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        pointerDownPosition = eventData.position;
        isDragging = false;
    }

    /// <summary>
    /// 开始拖拽：检查多点触控，置顶碎片
    /// </summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        // 多指触摸时不响应拖拽（避免双指缩放时误拖碎片）
        if (GameManager.Instance != null && GameManager.Instance.IsMultiTouch) return;
        if (Input.touchCount >= 2) return;
        isDragging = true;
        // 将碎片移到父容器最上层，防止被其他碎片遮挡
        transform.SetAsLastSibling();
    }

    /// <summary>
    /// 拖拽过程中：限制碎片位置在 PuzzleArea 内，并跟随指针移动
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        if (GameManager.Instance != null && GameManager.Instance.IsMultiTouch) return;
        if (Input.touchCount >= 2) return; // 双指时立即退出

        RectTransform puzzleArea = GameManager.Instance.puzzleArea;
        if (puzzleArea == null) return;

        // 获取 PuzzleArea 的世界坐标边界
        Vector3[] corners = new Vector3[4];
        puzzleArea.GetWorldCorners(corners);
        float minX = corners[0].x;
        float maxX = corners[2].x;
        float minY = corners[0].y;
        float maxY = corners[2].y;

        // 计算碎片实际显示尺寸的一半（考虑父物体缩放）
        Vector2 pieceSize = rectTransform.sizeDelta;
        Vector3 parentScale = rectTransform.parent.localScale;
        float halfWidth = pieceSize.x * parentScale.x * 0.5f;
        float halfHeight = pieceSize.y * parentScale.y * 0.5f;

        // 将屏幕坐标转换为世界坐标
        Vector3 worldPoint;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            puzzleArea, eventData.position, eventData.pressEventCamera, out worldPoint);

        // 限制位置在 PuzzleArea 内（边缘留出碎片半宽/半高）
        worldPoint.x = Mathf.Clamp(worldPoint.x, minX + halfWidth, maxX - halfWidth);
        worldPoint.y = Mathf.Clamp(worldPoint.y, minY + halfHeight, maxY - halfHeight);

        // 转换为父物体的本地坐标
        Vector3 localPos = rectTransform.parent.InverseTransformPoint(worldPoint);
        rectTransform.localPosition = localPos;
    }

    /// <summary>
    /// 结束拖拽：检查是否吸附到正确位置
    /// </summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        // 如果距离目标位置足够近且旋转正确，则锁定
        if (Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < snapDistance && currentRotation == 0)
        {
            LockPiece();
        }
        isDragging = false;
    }

    /// <summary>
    /// 指针抬起：如果移动距离小则视为点击，执行旋转
    /// </summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        if (!isDragging && Vector2.Distance(pointerDownPosition, eventData.position) < clickThreshold)
        {
            RotatePiece();
        }
    }

    // ==================== 旋转与锁定 ====================

    /// <summary>
    /// 顺时针旋转 90 度，如果旋转后位置正确则立即锁定
    /// </summary>
    private void RotatePiece()
    {
        currentRotation = (currentRotation + 90) % 360;
        rectTransform.localRotation = Quaternion.Euler(0, 0, currentRotation);
        // 如果旋转回 0 且位置正确，自动锁定
        if (currentRotation == 0 && Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < snapDistance)
        {
            LockPiece();
        }
    }

    /// <summary>
    /// 锁定碎片：设置到精确目标位置，旋转归零，变为粉色，并通知 GameManager
    /// </summary>
    private void LockPiece()
    {
        isLocked = true;
        rectTransform.anchoredPosition = targetPosition;
        rectTransform.localRotation = Quaternion.identity;
        currentRotation = 0;
        GetComponent<Image>().color = new Color(1f, 0.8f, 0.8f, 1f); // 淡粉色
        GameManager.Instance.CheckVictory();
    }

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 强制将碎片位置限制在 PuzzleArea 内（用于初始化时防止超出边界）
    /// </summary>
    public void ClampPositionToPuzzleArea()
    {
        RectTransform puzzleArea = GameManager.Instance.puzzleArea;
        if (puzzleArea == null) return;

        Vector3[] corners = new Vector3[4];
        puzzleArea.GetWorldCorners(corners);
        float minX = corners[0].x;
        float maxX = corners[2].x;
        float minY = corners[0].y;
        float maxY = corners[2].y;

        Vector2 pieceSize = rectTransform.sizeDelta;
        Vector3 parentScale = rectTransform.parent.localScale;
        float halfWidth = pieceSize.x * parentScale.x * 0.5f;
        float halfHeight = pieceSize.y * parentScale.y * 0.5f;

        Vector3 currentWorldPos = rectTransform.position;
        currentWorldPos.x = Mathf.Clamp(currentWorldPos.x, minX + halfWidth, maxX - halfWidth);
        currentWorldPos.y = Mathf.Clamp(currentWorldPos.y, minY + halfHeight, maxY - halfHeight);

        // 直接设置世界坐标，Unity 会自动转换本地坐标
        rectTransform.position = currentWorldPos;
    }
}