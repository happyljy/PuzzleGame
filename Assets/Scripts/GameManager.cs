using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Zoom & Pan Controls")]
    public Button zoomInButton;
    public Button zoomOutButton;
    public Button moveUpButton;
    public Button moveDownButton;
    public Button moveLeftButton;
    public Button moveRightButton;

    public float zoomStep = 0.1f;        // 每次缩放增量
    public float maxZoom = 2f;           // 最大缩放倍数
    public float moveStep = 20f;         // 每次移动像素距离

    private RectTransform puzzleContent; // 内部容器
    private float currentZoom = 1f;      // 当前缩放
    private Vector2 contentOffset;       // 当前移动偏移（相对于PuzzleArea中心）

    [Header("UI References")]
    public Button hintButton;                // 提示按钮

    private Image hintImage;                 // 当前显示的提示图

    [Header("UI References")]
    public Button returnButton;                 // 返回按钮

    [Header("List Piece Settings")]
    public float listPieceSize = 200f;          // 列表中碎片固定大小

    [Header("Grid Settings")]
    public Color gridLineColor = new Color(0f, 0f, 0f, 0.5f);   // 网格线颜色
    public float gridLineThickness = 2f;                       // 网格线粗细

    [Header("UI References")]
    //public Button startButton;
    public RectTransform listContent;          // ScrollView 的 Content
    public RectTransform puzzleArea;           // 拼图区域 Panel
    public GameObject victoryPanel;
    public GameObject piecePrefab;             // 碎片预制体

    [Header("Puzzle Settings")]
    public int minRows = 5;                    // 最低行列数
    public int minCols = 5;
    public float pieceSize = 100f;             // 碎片显示大小（正方形）
    public float spacingFactor = 1f;           // 列表中碎片间隔系数（1 表示一个碎片高度）

    private Sprite[] allSprites;               // 从 Resources/Art 加载的所有图片
    private Sprite chosenSprite;               // 当前使用的图片
    private int rows, cols;                    // 实际切割行列数
    private List<PuzzlePiece> activePieces = new List<PuzzlePiece>(); // 拼图区域中的碎片
    private int totalPieces;
    private int lockedCount = 0;

    void Awake()
    {
        Instance = this;
        //startButton.onClick.AddListener(StartNewGame);
        StartNewGame();
        returnButton.onClick.AddListener(ReturnUnlockedPieces);

        // 为提示按钮添加按下和抬起事件
        EventTrigger trigger = hintButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = hintButton.gameObject.AddComponent<EventTrigger>();

        // 按下显示提示图
        EventTrigger.Entry pointerDownEntry = new EventTrigger.Entry();
        pointerDownEntry.eventID = EventTriggerType.PointerDown;
        pointerDownEntry.callback.AddListener((data) => { ShowHint(); });
        trigger.triggers.Add(pointerDownEntry);

        // 抬起隐藏提示图
        EventTrigger.Entry pointerUpEntry = new EventTrigger.Entry();
        pointerUpEntry.eventID = EventTriggerType.PointerUp;
        pointerUpEntry.callback.AddListener((data) => { HideHint(); });
        trigger.triggers.Add(pointerUpEntry);

        // 缩放和移动按钮
        zoomInButton.onClick.AddListener(ZoomIn);
        zoomOutButton.onClick.AddListener(ZoomOut);
        moveUpButton.onClick.AddListener(() => MoveContent(Vector2.up));
        moveDownButton.onClick.AddListener(() => MoveContent(Vector2.down));
        moveLeftButton.onClick.AddListener(() => MoveContent(Vector2.left));
        moveRightButton.onClick.AddListener(() => MoveContent(Vector2.right));

        // 确保 PuzzleArea 有 RectMask2D 用于裁剪
        if (puzzleArea.GetComponent<RectMask2D>() == null)
        {
            puzzleArea.gameObject.AddComponent<RectMask2D>();
        }

        victoryPanel.SetActive(false);
    }

    void StartNewGame()
    {
        // 清除旧提示图
        if (hintImage != null)
        {
            Destroy(hintImage.gameObject);
            hintImage = null;
        }
        // 清空列表和拼图区域
        foreach (Transform child in listContent) Destroy(child.gameObject);
        foreach (Transform child in puzzleArea) Destroy(child.gameObject);
        activePieces.Clear();
        lockedCount = 0;
        victoryPanel.SetActive(false);

        // 清除旧网格
        Transform oldGrid = puzzleArea.Find("GridOverlay");
        if (oldGrid != null) Destroy(oldGrid.gameObject);

        // 创建或获取 PuzzleContent
        if (puzzleContent == null)
        {
            GameObject contentObj = new GameObject("PuzzleContent", typeof(RectTransform));
            contentObj.transform.SetParent(puzzleArea, false);
            puzzleContent = contentObj.GetComponent<RectTransform>();
            puzzleContent.anchorMin = new Vector2(0.5f, 0.5f);
            puzzleContent.anchorMax = new Vector2(0.5f, 0.5f);
            puzzleContent.pivot = new Vector2(0.5f, 0.5f);
            puzzleContent.sizeDelta = puzzleArea.sizeDelta;
            puzzleContent.anchoredPosition = Vector2.zero;
            puzzleContent.localScale = Vector3.one;
        }
        else
        {
            // 如果已存在，重置缩放和位置
            puzzleContent.localScale = Vector3.one;
            puzzleContent.anchoredPosition = Vector2.zero;
            currentZoom = 1f;
            contentOffset = Vector2.zero;
        }


        // 加载图片
        allSprites = Resources.LoadAll<Sprite>("Art");
        if (allSprites.Length == 0) { Debug.LogError("No sprites in Art"); return; }
        chosenSprite = allSprites[Random.Range(0, allSprites.Length)];
        Texture2D texture = chosenSprite.texture;

        // 计算行列数（保持最低 5x5）
        float aspect = (float)texture.width / texture.height;
        if (aspect >= 1f) { cols = Mathf.Max(minCols, Mathf.RoundToInt(minRows * aspect)); rows = minRows; }
        else { rows = Mathf.Max(minRows, Mathf.RoundToInt(minCols / aspect)); cols = minCols; }
        totalPieces = rows * cols;

        // 动态计算碎片大小
        float maxWidth = 1080f;
        float maxHeight = 700f;
        pieceSize = Mathf.Min(maxWidth / cols, maxHeight / rows);

        // 设置 PuzzleArea 锚点、轴心为中心，并调整其大小
        puzzleArea.anchorMin = new Vector2(0.5f, 0.5f);
        puzzleArea.anchorMax = new Vector2(0.5f, 0.5f);
        puzzleArea.pivot = new Vector2(0.5f, 0.5f);
        puzzleArea.sizeDelta = new Vector2(cols * pieceSize, rows * pieceSize);
        // ★ 设置 PuzzleArea 中心点位置
        puzzleArea.anchoredPosition = new Vector2(0f, 300f);

        // 切割纹理
        Sprite[] pieces = CutTexture(texture, rows, cols);

        // 生成碎片数据（随机旋转）
        List<PuzzlePieceData> pieceDataList = new List<PuzzlePieceData>();
        for (int i = 0; i < totalPieces; i++)
            pieceDataList.Add(new PuzzlePieceData(i, pieces[i], Random.Range(0, 4) * 90));

        Shuffle(pieceDataList);

        // 设置列表间距（碎片固定200，间距也固定）
        VerticalLayoutGroup layoutGroup = listContent.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup != null)
        {
            // 关键设置：不控制高度，防止拉伸
            layoutGroup.childControlWidth = true;       // 控制宽度（可保持200）
            layoutGroup.childControlHeight = false;     // 不控制高度，让子物体自己决定
            layoutGroup.childForceExpandWidth = false;  // 不拉伸宽度
            layoutGroup.childForceExpandHeight = false; // 不拉伸高度
            layoutGroup.spacing = listPieceSize;        // 间距设为200（或您想要的间距）
        }

        // 生成列表项
        foreach (var data in pieceDataList)
        {
            CreateListPiece(data.sprite, data.index, data.rotation);
        }

        // 预先计算每个碎片的目标位置（相对 PuzzleArea 中心）
        // 注意：这里只是计算，实际设置是在碎片创建时进行
        CreateGridOverlay();
    }

    /// <summary>
    /// 在列表中创建一个碎片项
    /// </summary>
    void CreateListPiece(Sprite sprite, int index, int rotation)
    {
        GameObject pieceObj = Instantiate(piecePrefab, listContent);
        PuzzlePiece piece = pieceObj.GetComponent<PuzzlePiece>();
        piece.Initialize(sprite, index, rotation);
        piece.interactable = false;   // 列表中不可交互

        RectTransform rt = pieceObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(listPieceSize, listPieceSize);

        // 添加 LayoutElement 固定尺寸
        LayoutElement layoutElement = pieceObj.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = listPieceSize;
        layoutElement.preferredHeight = listPieceSize;
        layoutElement.minWidth = listPieceSize;
        layoutElement.minHeight = listPieceSize;
        layoutElement.flexibleWidth = 0;
        layoutElement.flexibleHeight = 0;

        if (pieceObj.GetComponent<Button>() == null)
            pieceObj.AddComponent<Button>();
        pieceObj.GetComponent<Button>().onClick.AddListener(() => OnListPieceClicked(piece, pieceObj));
    }

    /// <summary>
    /// 显示提示图（在拼图区域底层创建半透明原图）
    /// </summary>
    public void ShowHint()
    {
        if (hintImage != null) return;  // 已显示

        GameObject hintObj = new GameObject("HintImage", typeof(RectTransform));
        hintObj.transform.SetParent(puzzleArea, false);
        hintObj.transform.SetAsFirstSibling();   // 放在最底层

        RectTransform hintRect = hintObj.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.5f, 0.5f);
        hintRect.anchorMax = new Vector2(0.5f, 0.5f);
        hintRect.pivot = new Vector2(0.5f, 0.5f);
        hintRect.sizeDelta = puzzleArea.sizeDelta;
        hintRect.anchoredPosition = Vector2.zero;

        Image img = hintObj.AddComponent<Image>();
        img.sprite = chosenSprite;
        img.color = new Color(1f, 1f, 1f, 0.3f);
        img.raycastTarget = false;

        hintImage = img;
    }

    /// <summary>
    /// 隐藏提示图
    /// </summary>
    public void HideHint()
    {
        if (hintImage != null)
        {
            Destroy(hintImage.gameObject);
            hintImage = null;
        }
    }

    /// <summary>
    /// 将所有未锁定的碎片从拼图区域返回到列表
    /// </summary>
    public void ReturnUnlockedPieces()
    {
        // 收集所有未锁定的碎片
        List<PuzzlePiece> unlockedPieces = new List<PuzzlePiece>();
        foreach (var piece in activePieces)
        {
            if (!piece.isLocked)
                unlockedPieces.Add(piece);
        }

        if (unlockedPieces.Count == 0) return;

        foreach (var piece in unlockedPieces)
        {
            // 获取碎片信息
            Sprite sprite = piece.GetComponent<Image>().sprite;
            int index = piece.pieceIndex;
            int rotation = piece.currentRotation;

            // 在列表中创建对应碎片
            CreateListPiece(sprite, index, rotation);

            // 从拼图区域移除该碎片
            activePieces.Remove(piece);
            Destroy(piece.gameObject);
        }

        // 注意：列表布局会自动更新，ContentSizeFitter 会重新计算
    }

    void OnListPieceClicked(PuzzlePiece listPiece, GameObject listObj)
    {
        GameObject newPieceObj = Instantiate(piecePrefab, puzzleContent);
        PuzzlePiece newPiece = newPieceObj.GetComponent<PuzzlePiece>();
        newPiece.Initialize(listPiece.GetComponent<Image>().sprite, listPiece.pieceIndex, listPiece.currentRotation);
        newPiece.interactable = true;

        // 计算目标位置
        int row = listPiece.pieceIndex / cols;
        int col = listPiece.pieceIndex % cols;
        float targetX = (col - (cols - 1) / 2f) * pieceSize;
        float targetY = ((rows - 1) / 2f - row) * pieceSize;
        newPiece.targetPosition = new Vector2(targetX, targetY);

        RectTransform rt = newPieceObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(pieceSize, pieceSize);

        // 设置随机初始位置（在 PuzzleArea 范围内）
        float halfW = puzzleArea.rect.width / 2f - pieceSize / 2f;
        float halfH = puzzleArea.rect.height / 2f - pieceSize / 2f;
        rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));

        // 移除按钮组件
        Button btn = newPieceObj.GetComponent<Button>();
        if (btn != null) Destroy(btn);

        // 确保初始位置在 PuzzleArea 内（考虑 PuzzleContent 可能的缩放偏移）
        newPiece.ClampPositionToPuzzleArea();

        activePieces.Add(newPiece);
        Destroy(listObj);
    }

    // 切割纹理为 Sprite 数组
    Sprite[] CutTexture(Texture2D texture, int rows, int cols)
    {
        Sprite[] sprites = new Sprite[rows * cols];
        int pieceWidth = texture.width / cols;
        int pieceHeight = texture.height / rows;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Rect rect = new Rect(c * pieceWidth, (rows - 1 - r) * pieceHeight, pieceWidth, pieceHeight);
                Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
                sprites[r * cols + c] = sprite;
            }
        }
        return sprites;
    }

    // 洗牌
    void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            T temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    // 检查胜利
    public void CheckVictory()
    {
        lockedCount++;
        if (lockedCount >= totalPieces)
        {
            victoryPanel.SetActive(true);
        }
    }
    void CreateGridOverlay()
    {
        // 创建网格容器
        GameObject gridObj = new GameObject("GridOverlay", typeof(RectTransform));
        gridObj.transform.SetParent(puzzleContent, false);
        RectTransform gridRect = gridObj.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.pivot = new Vector2(0.5f, 0.5f);
        gridRect.sizeDelta = puzzleArea.sizeDelta;   // 与拼图区域同尺寸
        gridRect.anchoredPosition = Vector2.zero;

        // 计算总宽高
        float totalWidth = cols * pieceSize;
        float totalHeight = rows * pieceSize;

        // 生成垂直线（共 cols+1 条）
        for (int i = 0; i <= cols; i++)
        {
            float x = -totalWidth / 2f + i * pieceSize;
            CreateGridLine(gridObj.transform, "VerticalLine_" + i,
                           new Vector2(gridLineThickness, totalHeight),
                           new Vector2(x, 0f));
        }

        // 生成水平线（共 rows+1 条）
        for (int i = 0; i <= rows; i++)
        {
            float y = totalHeight / 2f - i * pieceSize;   // 注意 Y 轴向上为正，所以从顶部开始
            CreateGridLine(gridObj.transform, "HorizontalLine_" + i,
                           new Vector2(totalWidth, gridLineThickness),
                           new Vector2(0f, y));
        }
    }

    void CreateGridLine(Transform parent, string name, Vector2 size, Vector2 anchoredPos)
    {
        GameObject lineObj = new GameObject(name, typeof(RectTransform));
        lineObj.transform.SetParent(parent, false);
        RectTransform lineRect = lineObj.GetComponent<RectTransform>();
        lineRect.sizeDelta = size;
        lineRect.anchoredPosition = anchoredPos;
        lineRect.anchorMin = new Vector2(0.5f, 0.5f);
        lineRect.anchorMax = new Vector2(0.5f, 0.5f);
        lineRect.pivot = new Vector2(0.5f, 0.5f);

        Image img = lineObj.AddComponent<Image>();
        img.color = gridLineColor;
        img.raycastTarget = false;   // 让线不阻挡点击
    }

    void ZoomIn()
    {
        currentZoom = Mathf.Min(currentZoom + zoomStep, maxZoom);
        ApplyContentTransform();
    }

    void ZoomOut()
    {
        currentZoom = Mathf.Max(currentZoom - zoomStep, 1f); // 最小为1
        ApplyContentTransform();
    }

    void MoveContent(Vector2 direction)
    {
        // 根据缩放调整移动步长（可选，也可以固定）
        contentOffset += direction * moveStep;
        ApplyContentTransform();
    }

    void ApplyContentTransform()
    {
        if (puzzleContent != null)
        {
            // 计算当前缩放后的内容尺寸
            float contentWidth = puzzleArea.sizeDelta.x * currentZoom;
            float contentHeight = puzzleArea.sizeDelta.y * currentZoom;

            // 计算允许的最大偏移量（内容边缘不能进入PuzzleArea内部）
            float maxOffsetX = Mathf.Max(0, (contentWidth - puzzleArea.sizeDelta.x) / 2f);
            float maxOffsetY = Mathf.Max(0, (contentHeight - puzzleArea.sizeDelta.y) / 2f);

            // 限制移动偏移
            contentOffset.x = Mathf.Clamp(contentOffset.x, -maxOffsetX, maxOffsetX);
            contentOffset.y = Mathf.Clamp(contentOffset.y, -maxOffsetY, maxOffsetY);

            // 应用缩放和位置
            puzzleContent.localScale = new Vector3(currentZoom, currentZoom, 1f);
            puzzleContent.anchoredPosition = contentOffset;
        }
    }

}



// 辅助数据结构
public class PuzzlePieceData
{
    public int index;
    public Sprite sprite;
    public int rotation;

    public PuzzlePieceData(int index, Sprite sprite, int rotation)
    {
        this.index = index;
        this.sprite = sprite;
        this.rotation = rotation;
    }
}