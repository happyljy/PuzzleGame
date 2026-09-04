using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PuzzlePiece : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [HideInInspector] public int pieceIndex;
    [HideInInspector] public int currentRotation;
    [HideInInspector] public bool isLocked = false;
    [HideInInspector] public Vector2 targetPosition;
    public bool interactable = true;

    private RectTransform rectTransform;
    private Vector2 pointerDownPosition;
    private bool isDragging = false;
    private const float clickThreshold = 10f;
    private const float snapDistance = 30f;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void Initialize(Sprite sprite, int index, int rotation)
    {
        GetComponent<Image>().sprite = sprite;
        pieceIndex = index;
        currentRotation = rotation;
        rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
        isLocked = false;
        GetComponent<Image>().color = Color.white;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        pointerDownPosition = eventData.position;
        isDragging = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        isDragging = true;
        // 将当前碎片移到最上层，避免被其他碎片遮挡
        transform.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;

        RectTransform puzzleArea = GameManager.Instance.puzzleArea;
        if (puzzleArea == null) return;

        // 获取 PuzzleArea 的世界坐标边界
        Vector3[] corners = new Vector3[4];
        puzzleArea.GetWorldCorners(corners);
        float minX = corners[0].x;
        float maxX = corners[2].x;
        float minY = corners[0].y;
        float maxY = corners[2].y;

        // 计算碎片实际显示尺寸的一半（考虑父物体的缩放）
        Vector2 pieceSize = rectTransform.sizeDelta;
        Vector3 parentScale = rectTransform.parent.localScale;
        float halfWidth = pieceSize.x * parentScale.x * 0.5f;
        float halfHeight = pieceSize.y * parentScale.y * 0.5f;

        // 将屏幕坐标转换为世界坐标（注意 out 参数为 Vector3）
        Vector3 worldPoint;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            puzzleArea, eventData.position, eventData.pressEventCamera, out worldPoint);

        // 限制位置在 PuzzleArea 内（减去碎片一半尺寸）
        worldPoint.x = Mathf.Clamp(worldPoint.x, minX + halfWidth, maxX - halfWidth);
        worldPoint.y = Mathf.Clamp(worldPoint.y, minY + halfHeight, maxY - halfHeight);

        // 转换为父物体的本地坐标
        Vector3 localPos = rectTransform.parent.InverseTransformPoint(worldPoint);
        rectTransform.localPosition = localPos;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        if (Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < snapDistance && currentRotation == 0)
        {
            LockPiece();
        }
        isDragging = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!interactable || isLocked) return;
        if (!isDragging && Vector2.Distance(pointerDownPosition, eventData.position) < clickThreshold)
        {
            RotatePiece();
        }
    }

    private void RotatePiece()
    {
        currentRotation = (currentRotation + 90) % 360;
        rectTransform.localRotation = Quaternion.Euler(0, 0, currentRotation);
        if (currentRotation == 0 && Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < snapDistance)
        {
            LockPiece();
        }
    }

    private void LockPiece()
    {
        isLocked = true;
        rectTransform.anchoredPosition = targetPosition;
        rectTransform.localRotation = Quaternion.identity;
        currentRotation = 0;
        GetComponent<Image>().color = new Color(1f, 0.8f, 0.8f, 1f);
        GameManager.Instance.CheckVictory();
    }

    // 新增方法：强制将碎片位置限制在 PuzzleArea 内（用于初始化）
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

        // 直接设置世界坐标即可，Unity 会自动转换本地坐标
        rectTransform.position = currentWorldPos;
    }
}