using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 游戏场景主管理器：负责拼图初始化、碎片生成、交互控制、
/// 每日拼图逻辑、奖励结算、收藏、提示、缩放平移等所有功能。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // ==================== UI 引用 ====================

    [Header("UI References")]
    public Button favoriteButton;           // 收藏按钮
    public Button nextImageButton;          // 下一张按钮
    public Button prevImageButton;          // 上一张按钮
    public Button backButton;               // 返回主菜单按钮
    public Button hintButton;               // 提示按钮（按住显示原图）
    public Button returnButton;             // 返回碎片按钮（将未锁定碎片放回列表）
    public RectTransform listContent;       // 碎片列表的 Content（水平布局）
    public RectTransform puzzleArea;        // 拼图区域 Panel
    public GameObject victoryPanel;         // 胜利面板
    public GameObject piecePrefab;          // 碎片预制体
    public Text rewardText;                 // 胜利面板中的奖励文字

    [Header("确认弹窗")]
    public GameObject confirmPanel;         // 通用确认弹窗 Panel
    public Text confirmText;                // 弹窗提示文字
    public Button confirmYesButton;         // 确定按钮
    public Button confirmNoButton;          // 取消按钮

    // ==================== 游戏流程参数 ====================

    [Header("Game Flow")]
    public float easyTimeLimit = 120f;      // 简单难度时间限制（秒）
    public float normalTimeLimit = 240f;    // 普通难度时间限制（秒）
    public float hardTimeLimit = 360f;      // 困难难度时间限制（秒）

    [Header("List Piece Settings")]
    public float listPieceSize = 200f;      // 列表中碎片固定大小

    [Header("Grid Settings")]
    public Color gridLineColor = new Color(0f, 0f, 0f, 0.5f);  // 网格线颜色
    public float gridLineThickness = 2f;                       // 网格线粗细

    [Header("Puzzle Settings")]
    public float pieceSize = 100f;          // 碎片显示大小（运行时动态计算）
    public float spacingFactor = 1f;        // 列表中碎片间隔系数

    // ==================== 私有状态 ====================

    private bool isDailyPuzzle = false;               // 是否为每日拼图模式
    private int currentImageIndex = -1;               // 当前实际使用的图片索引
    private string selectedCategory;                  // 当前分类
    private int selectedImageIndex = -1;              // 选中的图片索引（-1为随机）
    private int gridSize = 2;                         // 难度（行数=列数，测试用2，正式可6/8/10）
    private float timeRemaining;                      // 剩余时间
    private bool isVictory = false;                   // 是否已完成当前拼图

    private RectTransform puzzleContent;              // 拼图内容容器（碎片和网格的父物体）
    private float currentZoom = 1f;                   // 当前缩放倍数
    private Vector2 contentOffset;                    // 平移偏移量
    private float maxZoom = 2f;                       // 最大缩放倍数
    private float minZoom = 1f;                       // 最小缩放倍数

    private Sprite[] allSprites;                      // 当前分类下的所有图片
    private Sprite chosenSprite;                      // 当前选中的图片
    private int rows, cols;                           // 实际行列数
    private List<PuzzlePiece> activePieces = new List<PuzzlePiece>(); // 拼图区域中的碎片列表
    private int totalPieces;                          // 总碎片数量
    private int lockedCount = 0;                      // 已锁定的碎片数量

    private Image hintImage;                          // 提示图（半透明原图）
    private bool isMultiTouch = false;                // 是否多指触摸
    public bool IsMultiTouch => isMultiTouch;         // 供外部访问的属性

    private System.Action confirmAction;              // 确认弹窗的回调
    private int dailyPuzzleCurrentIndex = -1;         // 当前每日拼图在列表中的索引

    // ==================== 初始化 ====================

    void Awake()
    {
        Instance = this;

        // 绑定按钮事件
        favoriteButton.onClick.AddListener(ToggleFavorite);
        nextImageButton.onClick.AddListener(NextImage);
        prevImageButton.onClick.AddListener(PrevImage);
        backButton.onClick.AddListener(BackToMenu);
        returnButton.onClick.AddListener(ReturnUnlockedPieces);

        // 提示按钮：按下显示原图，抬起隐藏
        EventTrigger trigger = hintButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = hintButton.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry pointerDownEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDownEntry.callback.AddListener((data) => { ShowHint(); });
        trigger.triggers.Add(pointerDownEntry);
        EventTrigger.Entry pointerUpEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUpEntry.callback.AddListener((data) => { HideHint(); });
        trigger.triggers.Add(pointerUpEntry);

        // 确认弹窗按钮
        confirmYesButton.onClick.AddListener(OnConfirmYes);
        confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);

        // 给 PuzzleArea 添加 RectMask2D 实现裁剪
        if (puzzleArea.GetComponent<RectMask2D>() == null)
            puzzleArea.gameObject.AddComponent<RectMask2D>();

        victoryPanel.SetActive(false);
    }

    void Start()
    {
        // 读取每日拼图标记
        isDailyPuzzle = PlayerPrefs.GetInt("IsDailyPuzzle", 0) == 1;

        if (isDailyPuzzle)
        {
            // ===== 每日拼图模式初始化 =====
            // 兼容旧版进度：使用旧版进度作为当前索引的初始值
            int progress = GameDataManager.GetDailyPuzzleProgress();
            List<string> images = GameDataManager.GetDailyPuzzleImages();
            List<int> difficulties = GameDataManager.GetDailyPuzzleDifficulties();

            if (images.Count == 0)
            {
                Debug.LogError("每日拼图数据为空");
                BackToMenu();
                return;
            }

            // 如果旧版进度为 -1，从第一张开始（索引0）
            dailyPuzzleCurrentIndex = progress < 0 ? 0 : progress;
            if (dailyPuzzleCurrentIndex >= images.Count) dailyPuzzleCurrentIndex = images.Count - 1;

            // 获取当前图片和难度
            string[] parts = images[dailyPuzzleCurrentIndex].Split('_');
            selectedCategory = parts[0];
            selectedImageIndex = int.Parse(parts[1]);
            gridSize = difficulties[dailyPuzzleCurrentIndex];
        }
        else
        {
            // ===== 普通模式初始化 =====
            selectedCategory = PlayerPrefs.GetString("SelectedCategory", "1");
            gridSize = PlayerPrefs.GetInt("Difficulty", 6);
            selectedImageIndex = PlayerPrefs.GetInt("SelectedImageIndex", -1);
        }

        // 根据难度设置时间限制
        switch (gridSize)
        {
            case 2: timeRemaining = easyTimeLimit; break;   // 测试用
            case 8: timeRemaining = normalTimeLimit; break;
            case 10: timeRemaining = hardTimeLimit; break;
            default: timeRemaining = easyTimeLimit; break;
        }

        Debug.Log("当前难度 gridSize = " + gridSize);
        StartNewGame();
    }

    void Update()
    {
        // 倒计时
        if (!isVictory)
        {
            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0) timeRemaining = 0;
        }

        // 处理多指缩放和平移
        HandleTouchInput();
    }

    // ==================== 拼图初始化 ====================

    /// <summary>
    /// 开始新拼图：加载图片、切割、生成碎片列表和网格
    /// </summary>
    void StartNewGame()
    {
        // 检查体力（每日模式不消耗）
        if (!isDailyPuzzle && GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            Debug.Log("体力不足，无法开始拼图");
            BackToMenu();
            return;
        }

        // 重置胜利状态
        isVictory = false;
        victoryPanel.SetActive(false);
        if (rewardText != null) rewardText.text = "";

        // 清理旧提示图
        if (hintImage != null) { Destroy(hintImage.gameObject); hintImage = null; }

        // 清空碎片列表
        foreach (Transform child in listContent) Destroy(child.gameObject);

        // 销毁旧的 puzzleContent（包括碎片和网格），并置空引用
        if (puzzleContent != null)
        {
            Destroy(puzzleContent.gameObject);
            puzzleContent = null;
        }

        // 重置活动碎片和锁定计数
        activePieces.Clear();
        lockedCount = 0;
        victoryPanel.SetActive(false);

        // 加载分类文件夹下的所有图片
        string folderPath = "Art/" + selectedCategory;
        allSprites = Resources.LoadAll<Sprite>(folderPath);
        if (allSprites.Length == 0)
        {
            Debug.LogError("没有找到图片: " + folderPath);
            return;
        }
        System.Array.Sort(allSprites, (a, b) => string.Compare(a.name, b.name));

        // 选择图片：优先使用指定索引，否则随机
        if (selectedImageIndex >= 0 && selectedImageIndex < allSprites.Length)
        {
            chosenSprite = allSprites[selectedImageIndex];
            currentImageIndex = selectedImageIndex;
        }
        else
        {
            currentImageIndex = Random.Range(0, allSprites.Length);
            chosenSprite = allSprites[currentImageIndex];
            selectedImageIndex = currentImageIndex;
        }

        Texture2D texture = chosenSprite.texture;
        UpdateFavoriteButtonColor();

        // 固定行列数（正方形网格）
        rows = gridSize;
        cols = gridSize;
        totalPieces = rows * cols;

        // 计算碎片大小，使总宽高不超过限制
        float maxWidth = 1080f;
        float maxHeight = 700f;
        pieceSize = Mathf.Min(maxWidth / cols, maxHeight / rows);

        // 设置 PuzzleArea 的大小和位置
        puzzleArea.anchorMin = new Vector2(0.5f, 0.5f);
        puzzleArea.anchorMax = new Vector2(0.5f, 0.5f);
        puzzleArea.pivot = new Vector2(0.5f, 0.5f);
        puzzleArea.sizeDelta = new Vector2(cols * pieceSize, rows * pieceSize);
        puzzleArea.anchoredPosition = new Vector2(0f, 300f);

        // 创建新的 puzzleContent（放在 PuzzleArea 下面）
        GameObject contentObj = new GameObject("PuzzleContent", typeof(RectTransform));
        contentObj.transform.SetParent(puzzleArea, false);
        puzzleContent = contentObj.GetComponent<RectTransform>();
        puzzleContent.anchorMin = new Vector2(0.5f, 0.5f);
        puzzleContent.anchorMax = new Vector2(0.5f, 0.5f);
        puzzleContent.pivot = new Vector2(0.5f, 0.5f);
        puzzleContent.sizeDelta = puzzleArea.sizeDelta;
        puzzleContent.anchoredPosition = Vector2.zero;
        puzzleContent.localScale = Vector3.one;
        currentZoom = 1f;
        contentOffset = Vector2.zero;

        // 如果每日拼图的该图片已完成，直接显示完整图
        if (isDailyPuzzle && GameDataManager.IsDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex))
        {
            GenerateCompletedPuzzle(texture);
            return;
        }

        // 切割纹理为碎片 Sprite
        Sprite[] pieces = CutTexture(texture, rows, cols);

        // 随机旋转并打乱碎片
        List<PuzzlePieceData> pieceDataList = new List<PuzzlePieceData>();
        for (int i = 0; i < totalPieces; i++)
            pieceDataList.Add(new PuzzlePieceData(i, pieces[i], Random.Range(0, 4) * 90));
        Shuffle(pieceDataList);

        // 设置列表水平布局
        listContent.anchorMin = new Vector2(0, 0.5f);
        listContent.anchorMax = new Vector2(0, 0.5f);
        listContent.pivot = new Vector2(0, 0.5f);
        listContent.sizeDelta = new Vector2(0, listPieceSize);

        HorizontalLayoutGroup layoutGroup = listContent.GetComponent<HorizontalLayoutGroup>();
        if (layoutGroup == null) layoutGroup = listContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.childControlWidth = false;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.spacing = listPieceSize;
        layoutGroup.childAlignment = TextAnchor.MiddleLeft;
        layoutGroup.padding = new RectOffset(0, 0, 0, 0);

        ContentSizeFitter fitter = listContent.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        // 生成列表项
        foreach (var data in pieceDataList)
        {
            CreateListPiece(data.sprite, data.index, data.rotation);
        }

        // 重建布局，确保 Content 宽度正确
        LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
        listContent.anchoredPosition = Vector2.zero;

        // 生成网格线
        CreateGridOverlay();
    }

    /// <summary>
    /// 生成已完成拼图的完整图显示（用于每日拼图已完成的图片）
    /// </summary>
    void GenerateCompletedPuzzle(Texture2D texture)
    {
        // 隐藏胜利面板和提示
        victoryPanel.SetActive(false);
        if (rewardText != null) rewardText.text = "";

        // 创建完整图片
        GameObject completeImg = new GameObject("CompletedImage", typeof(RectTransform));
        completeImg.transform.SetParent(puzzleContent, false);
        RectTransform imgRect = completeImg.GetComponent<RectTransform>();
        imgRect.anchorMin = new Vector2(0.5f, 0.5f);
        imgRect.anchorMax = new Vector2(0.5f, 0.5f);
        imgRect.pivot = new Vector2(0.5f, 0.5f);
        imgRect.sizeDelta = new Vector2(cols * pieceSize, rows * pieceSize);
        imgRect.anchoredPosition = Vector2.zero;
        Image img = completeImg.AddComponent<Image>();
        img.sprite = chosenSprite;
        img.color = Color.white;
        img.raycastTarget = false;
    }

    // ==================== 网格线 ====================

    void CreateGridOverlay()
    {
        GameObject gridObj = new GameObject("GridOverlay", typeof(RectTransform));
        gridObj.transform.SetParent(puzzleContent, false);
        RectTransform gridRect = gridObj.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.pivot = new Vector2(0.5f, 0.5f);
        gridRect.sizeDelta = puzzleArea.sizeDelta;
        gridRect.anchoredPosition = Vector2.zero;

        float totalWidth = cols * pieceSize;
        float totalHeight = rows * pieceSize;

        // 垂直线
        for (int i = 0; i <= cols; i++)
        {
            float x = -totalWidth / 2f + i * pieceSize;
            CreateGridLine(gridObj.transform, "VerticalLine_" + i,
                           new Vector2(gridLineThickness, totalHeight),
                           new Vector2(x, 0f));
        }

        // 水平线
        for (int i = 0; i <= rows; i++)
        {
            float y = totalHeight / 2f - i * pieceSize;
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
        img.raycastTarget = false;   // 不阻挡点击
    }

    // ==================== 碎片列表创建 ====================

    void CreateListPiece(Sprite sprite, int index, int rotation)
    {
        GameObject pieceObj = Instantiate(piecePrefab, listContent);
        PuzzlePiece piece = pieceObj.GetComponent<PuzzlePiece>();
        piece.Initialize(sprite, index, rotation);
        piece.interactable = false; // 列表中不可交互
        RectTransform rt = pieceObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(listPieceSize, listPieceSize);
        LayoutElement layoutElement = pieceObj.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = listPieceSize;
        layoutElement.preferredHeight = listPieceSize;
        layoutElement.minWidth = listPieceSize;
        layoutElement.minHeight = listPieceSize;
        layoutElement.flexibleWidth = 0;
        layoutElement.flexibleHeight = 0;
        if (pieceObj.GetComponent<Button>() == null) pieceObj.AddComponent<Button>();
        pieceObj.GetComponent<Button>().onClick.AddListener(() => OnListPieceClicked(piece, pieceObj));
    }

    void OnListPieceClicked(PuzzlePiece listPiece, GameObject listObj)
    {
        // 在拼图区域生成可交互碎片
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

        // 随机初始位置
        float halfW = puzzleArea.rect.width / 2f - pieceSize / 2f;
        float halfH = puzzleArea.rect.height / 2f - pieceSize / 2f;
        rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));

        // 移除按钮组件（避免与拖拽冲突）
        Button btn = newPieceObj.GetComponent<Button>();
        if (btn != null) Destroy(btn);

        newPiece.ClampPositionToPuzzleArea();
        activePieces.Add(newPiece);
        Destroy(listObj);
    }

    // ==================== 工具方法 ====================

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
                sprites[r * cols + c] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
            }
        }
        return sprites;
    }

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

    // ==================== 胜利结算 ====================

    public void CheckVictory()
    {
        lockedCount++;
        if (lockedCount >= totalPieces)
        {
            isVictory = true;

            if (isDailyPuzzle)
            {
                // 标记当前图片已完成
                GameDataManager.SetDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex, true);

                // 检查是否所有图片已完成
                List<bool> flags = GameDataManager.GetDailyPuzzleCompletedFlags();
                int completedCount = 0;
                foreach (bool b in flags) if (b) completedCount++;

                if (completedCount >= GameDataManager.DailyPuzzleCount)
                {
                    // 全部完成，发放奖励
                    int dailyReward = 50;
                    GameDataManager.AddCoins(dailyReward);
                    GameDataManager.SetDailyPuzzleCompleted();
                    PlayerPrefs.SetInt("IsDailyPuzzle", 0);
                    PlayerPrefs.Save();
                    if (rewardText != null) rewardText.text = $"恭喜完成每日拼图！获得金币：{dailyReward}";
                    victoryPanel.SetActive(true);
                }
                else
                {
                    // 未全部完成，显示提示
                    if (rewardText != null) rewardText.text = "本图已完成，可点击下一张/上一张继续";
                    victoryPanel.SetActive(true);
                }
            }
            else
            {
                // 普通模式：消耗体力
                GameDataManager.ConsumeStamina(GameDataManager.PuzzleStaminaCost);

                int baseReward = 0;
                int experienceReward = 0;
                switch (gridSize)
                {
                    case 2: // 测试用
                        baseReward = 5;
                        experienceReward = 3;
                        break;
                    case 8:
                        baseReward = 10;
                        experienceReward = 5;
                        break;
                    case 10:
                        baseReward = 15;
                        experienceReward = 8;
                        break;
                }

                int bonus = (timeRemaining > 0) ? 3 : 0;
                int totalReward = baseReward + bonus;
                bool firstTime = !GameDataManager.HasClaimedReward(selectedCategory, currentImageIndex, gridSize);

                GameDataManager.AddExperience(experienceReward);
                if (firstTime)
                {
                    GameDataManager.AddCoins(totalReward);
                    GameDataManager.SetRewardClaimed(selectedCategory, currentImageIndex, gridSize);
                }

                if (rewardText != null)
                {
                    if (firstTime)
                    {
                        rewardText.text = $"获得金币：{baseReward}";
                        if (bonus > 0) rewardText.text += $" + 限时奖励 {bonus} = {totalReward}";
                    }
                    else
                    {
                        rewardText.text = "重复完成，仅获得经验";
                    }
                }
                victoryPanel.SetActive(true);
            }
        }
    }

    // ==================== 提示图 ====================

    public void ShowHint()
    {
        if (hintImage != null) return;
        GameObject hintObj = new GameObject("HintImage", typeof(RectTransform));
        hintObj.transform.SetParent(puzzleArea, false);
        hintObj.transform.SetAsFirstSibling();
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

    public void HideHint()
    {
        if (hintImage != null)
        {
            Destroy(hintImage.gameObject);
            hintImage = null;
        }
    }

    // ==================== 返回碎片 ====================

    public void ReturnUnlockedPieces()
    {
        List<PuzzlePiece> unlockedPieces = new List<PuzzlePiece>();
        foreach (var piece in activePieces)
        {
            if (!piece.isLocked) unlockedPieces.Add(piece);
        }

        foreach (var piece in unlockedPieces)
        {
            Sprite sprite = piece.GetComponent<Image>().sprite;
            int index = piece.pieceIndex;
            int rotation = piece.currentRotation;
            CreateListPiece(sprite, index, rotation);
            activePieces.Remove(piece);
            Destroy(piece.gameObject);
        }
    }

    // ==================== 上一张/下一张 ====================

    public void NextImage()
    {
        if (isDailyPuzzle)
        {
            int count = GameDataManager.GetDailyPuzzleImages().Count;
            if (count == 0) return;
            dailyPuzzleCurrentIndex = (dailyPuzzleCurrentIndex + 1) % count;
            LoadDailyPuzzleImage(dailyPuzzleCurrentIndex);
            return;
        }

        // 普通模式
        if (allSprites == null || allSprites.Length == 0) return;
        if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            ShowConfirm("体力不足，无法切换图片", null);
            return;
        }
        if (currentImageIndex == allSprites.Length - 1)
        {
            ShowConfirm("已经到最后一章，是否直接到第一张？", () =>
            {
                selectedImageIndex = 0;
                StartNewGame();
            });
        }
        else
        {
            selectedImageIndex = currentImageIndex + 1;
            StartNewGame();
        }
    }

    public void PrevImage()
    {
        if (isDailyPuzzle)
        {
            int count = GameDataManager.GetDailyPuzzleImages().Count;
            if (count == 0) return;
            dailyPuzzleCurrentIndex = (dailyPuzzleCurrentIndex - 1 + count) % count;
            LoadDailyPuzzleImage(dailyPuzzleCurrentIndex);
            return;
        }

        // 普通模式
        if (allSprites == null || allSprites.Length == 0) return;
        if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            ShowConfirm("体力不足，无法切换图片", null);
            return;
        }
        if (currentImageIndex == 0)
        {
            ShowConfirm("已经到第一章，是否直接到最后一张？", () =>
            {
                selectedImageIndex = allSprites.Length - 1;
                StartNewGame();
            });
        }
        else
        {
            selectedImageIndex = currentImageIndex - 1;
            StartNewGame();
        }
    }

    void LoadDailyPuzzleImage(int index)
    {
        List<string> images = GameDataManager.GetDailyPuzzleImages();
        List<int> difficulties = GameDataManager.GetDailyPuzzleDifficulties();
        if (index < 0 || index >= images.Count) return;

        string[] parts = images[index].Split('_');
        selectedCategory = parts[0];
        selectedImageIndex = int.Parse(parts[1]);
        gridSize = difficulties[index];
        dailyPuzzleCurrentIndex = index;
        StartNewGame();
    }

    // ==================== 确认弹窗 ====================

    void ShowConfirm(string message, System.Action onConfirm)
    {
        confirmText.text = message;
        confirmAction = onConfirm;
        confirmPanel.SetActive(true);
    }

    void OnConfirmYes()
    {
        confirmPanel.SetActive(false);
        confirmAction?.Invoke();
    }

    // ==================== 收藏 ====================

    private void UpdateFavoriteButtonColor()
    {
        if (favoriteButton == null || selectedCategory == null || currentImageIndex < 0) return;
        Image buttonImage = favoriteButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = GameDataManager.IsFavorite(selectedCategory, currentImageIndex)
                ? new Color(1f, 0.84f, 0f, 1f)  // 金色
                : Color.white;
        }
    }

    void ToggleFavorite()
    {
        if (selectedCategory == null || currentImageIndex < 0) return;
        if (GameDataManager.IsFavorite(selectedCategory, currentImageIndex))
            GameDataManager.RemoveFavorite(selectedCategory, currentImageIndex);
        else
            GameDataManager.AddFavorite(selectedCategory, currentImageIndex);
        UpdateFavoriteButtonColor();
    }

    // ==================== 触摸缩放与平移 ====================

    void HandleTouchInput()
    {
        if (Input.touchCount == 1)
        {
            isMultiTouch = false;
        }
        else if (Input.touchCount >= 2)
        {
            isMultiTouch = true;
            Touch touch0 = Input.GetTouch(0);
            Touch touch1 = Input.GetTouch(1);

            if (!IsPointOverPuzzleArea(touch0.position) || !IsPointOverPuzzleArea(touch1.position))
                return;

            Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
            Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;
            float prevDistance = Vector2.Distance(touch0PrevPos, touch1PrevPos);
            float currentDistance = Vector2.Distance(touch0.position, touch1.position);

            if (prevDistance > 0.001f)
            {
                float zoomFactor = currentDistance / prevDistance;
                currentZoom = Mathf.Clamp(currentZoom * zoomFactor, minZoom, maxZoom);
            }

            Vector2 prevMidpoint = (touch0PrevPos + touch1PrevPos) / 2f;
            Vector2 currentMidpoint = (touch0.position + touch1.position) / 2f;

            Vector2 localCurrent, localPrev;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(puzzleArea, currentMidpoint, null, out localCurrent);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(puzzleArea, prevMidpoint, null, out localPrev);
            Vector2 localDelta = localCurrent - localPrev;

            contentOffset += localDelta;
            ApplyContentTransform();
        }
        else
        {
            isMultiTouch = false;
        }
    }

    bool IsPointOverPuzzleArea(Vector2 screenPoint)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(puzzleArea, screenPoint, null);
    }

    void ApplyContentTransform()
    {
        if (puzzleContent == null) return;
        float contentWidth = puzzleArea.sizeDelta.x * currentZoom;
        float contentHeight = puzzleArea.sizeDelta.y * currentZoom;
        float maxOffsetX = Mathf.Max(0, (contentWidth - puzzleArea.sizeDelta.x) / 2f);
        float maxOffsetY = Mathf.Max(0, (contentHeight - puzzleArea.sizeDelta.y) / 2f);
        contentOffset.x = Mathf.Clamp(contentOffset.x, -maxOffsetX, maxOffsetX);
        contentOffset.y = Mathf.Clamp(contentOffset.y, -maxOffsetY, maxOffsetY);
        puzzleContent.localScale = new Vector3(currentZoom, currentZoom, 1f);
        puzzleContent.anchoredPosition = contentOffset;
    }

    // ==================== 返回主菜单 ====================

    void BackToMenu()
    {
        PlayerPrefs.SetInt("IsDailyPuzzle", 0);
        PlayerPrefs.Save();
        SceneManager.LoadScene("LevelScene");
    }
}

// 辅助数据结构：碎片数据
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