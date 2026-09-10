using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// 游戏场景主管理器：负责拼图初始化、碎片生成、交互控制、
/// 每日拼图逻辑、奖励结算、收藏、提示、缩放平移等所有功能。
/// </summary>
public class GameManager : MonoBehaviour
{
    #region 单例

    public static GameManager Instance { get; private set; }

    #endregion

    #region UI 引用与配置

    [Header("音效")]
    public AudioClip victorySound;          // 胜利音效
    public AudioClip dailyCompleteSound;    // 每日拼图全部完成音效

    [Header("UI References")]
    public Button favoriteButton;           // 收藏按钮
    public Button nextImageButton;          // 下一张按钮
    public Button prevImageButton;          // 上一张按钮
    public Button backButton;               // 返回主菜单按钮
    public Button hintButton;               // 提示按钮（按住显示原图）
    public Button returnButton;             // 返回碎片按钮
    public RectTransform listContent;       // 碎片列表 Content
    public RectTransform puzzleArea;        // 拼图区域
    public GameObject victoryPanel;         // 胜利面板
    public GameObject piecePrefab;          // 碎片预制体
    public Text rewardText;                 // 胜利面板奖励文字

    [Header("确认弹窗")]
    public GameObject confirmPanel;
    public Text confirmText;
    public Button confirmYesButton;
    public Button confirmNoButton;

    [Header("Game Flow")]
    public float easyTimeLimit = 120f;
    public float normalTimeLimit = 240f;
    public float hardTimeLimit = 360f;

    [Header("List Piece Settings")]
    public float listPieceSize = 200f;

    [Header("Grid Settings")]
    public Color gridLineColor = new Color(0f, 0f, 0f, 0.5f);
    public float gridLineThickness = 2f;

    [Header("Puzzle Settings")]
    public float pieceSize = 100f;
    public float spacingFactor = 1f;

    #endregion

    #region 对外属性

    /// <summary>已锁定的碎片数量（供 PuzzlePiece 访问）。</summary>
    public int LockedCount => lockedCount;

    /// <summary>碎片总数（供 PuzzlePiece 访问）。</summary>
    public int TotalPieces => totalPieces;

    /// <summary>是否处于多点触控状态。</summary>
    public bool IsMultiTouch => isMultiTouch;

    #endregion

    #region 私有状态

    private bool isDailyPuzzle = false;         // 是否为每日拼图模式
    private int currentImageIndex = -1;         // 当前实际使用的图片索引
    private string selectedCategory;            // 当前分类
    private int selectedImageIndex = -1;        // 用户选中的图片索引（-1 为随机）
    private int gridSize = 2;                   // 难度（行列数），测试用 2，正式可 6/8/10
    private float timeRemaining;                // 剩余时间
    private bool isVictory = false;             // 是否已胜利

    private RectTransform puzzleContent;        // 拼图内容容器（碎片和网格的父物体）
    private float currentZoom = 1f;             // 当前缩放倍数
    private Vector2 contentOffset;              // 平移偏移量
    private float maxZoom = 2f;
    private float minZoom = 1f;

    private Sprite[] allSprites;                // 当前分类下的所有图片（普通分类）
    private Sprite chosenSprite;                // 当前选中的图片
    private int rows, cols;                     // 实际行列数
    private List<PuzzlePiece> activePieces = new List<PuzzlePiece>();   // 拼图区域中的碎片
    private int totalPieces;                    // 总碎片数
    private int lockedCount = 0;                // 已锁定碎片数

    private Image hintImage;                    // 提示图
    private bool isMultiTouch = false;          // 是否多点触控

    private System.Action confirmAction;        // 确认弹窗回调
    private int dailyPuzzleCurrentIndex = -1;   // 每日拼图当前索引

    #endregion

    #region Unity 生命周期

    private void Awake()
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

        // 确认弹窗
        confirmYesButton.onClick.AddListener(OnConfirmYes);
        confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);

        // 为拼图区域添加裁剪
        if (puzzleArea.GetComponent<RectMask2D>() == null)
            puzzleArea.gameObject.AddComponent<RectMask2D>();

        victoryPanel.SetActive(false);
    }

    private void Start()
    {
        isDailyPuzzle = PlayerPrefs.GetInt("IsDailyPuzzle", 0) == 1;

        if (isDailyPuzzle)
        {
            // 每日拼图模式
            List<string> images = GameDataManager.GetDailyPuzzleImages();
            List<int> difficulties = GameDataManager.GetDailyPuzzleDifficulties();

            if (images.Count == 0)
            {
                Debug.LogError("每日拼图数据为空");
                BackToMenu();
                return;
            }

            dailyPuzzleCurrentIndex = 0;
            if (dailyPuzzleCurrentIndex >= images.Count) dailyPuzzleCurrentIndex = 0;

            string[] parts = images[dailyPuzzleCurrentIndex].Split('_');
            selectedCategory = parts[0];
            selectedImageIndex = int.Parse(parts[1]);
            gridSize = difficulties[dailyPuzzleCurrentIndex];
        }
        else
        {
            // 普通模式
            selectedCategory = PlayerPrefs.GetString("SelectedCategory", "1");
            gridSize = PlayerPrefs.GetInt("Difficulty", 6);
            selectedImageIndex = PlayerPrefs.GetInt("SelectedImageIndex", -1);
        }

        // 设置时间限制
        switch (gridSize)
        {
            case 2: timeRemaining = easyTimeLimit; break;
            case 8: timeRemaining = normalTimeLimit; break;
            case 10: timeRemaining = hardTimeLimit; break;
            default: timeRemaining = easyTimeLimit; break;
        }

        StartNewGame();
    }

    private void Update()
    {
        // 倒计时
        if (!isVictory)
        {
            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0) timeRemaining = 0;
        }

        HandleTouchInput();
    }

    #endregion

    #region 游戏初始化

    /// <summary>
    /// 开始新拼图：加载图片、切割、生成碎片列表和网格。
    /// </summary>
    private void StartNewGame()
    {
        // 体力检查（每日拼图不消耗）
        if (!isDailyPuzzle && GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            Debug.Log("体力不足，无法开始拼图");
            BackToMenu();
            return;
        }

        // 重置状态
        isVictory = false;
        victoryPanel.SetActive(false);
        if (rewardText != null) rewardText.text = "";

        if (hintImage != null) { Destroy(hintImage.gameObject); hintImage = null; }
        foreach (Transform child in listContent) Destroy(child.gameObject);
        if (puzzleContent != null) { Destroy(puzzleContent.gameObject); puzzleContent = null; }
        activePieces.Clear();
        lockedCount = 0;

        // ==================== 加载图片 ====================
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            // 上传分类：从 Uploads 文件夹加载
            List<string> uploadFiles = GameDataManager.GetUploadedImages();
            if (selectedImageIndex < 0 || selectedImageIndex >= uploadFiles.Count)
            {
                Debug.LogError("上传图片索引无效");
                BackToMenu();
                return;
            }
            string fileName = uploadFiles[selectedImageIndex];
            string path = GameDataManager.GetUploadedImagePath(fileName);
            if (!File.Exists(path))
            {
                Debug.LogError("上传图片文件不存在: " + path);
                BackToMenu();
                return;
            }
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            chosenSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            currentImageIndex = selectedImageIndex;
        }
        else if (selectedCategory == GameDataManager.SharedCategory)
        {
            // 共享分类：从 Shared 文件夹加载
            List<string> sharedFiles = GameDataManager.GetSharedImages();
            if (selectedImageIndex < 0 || selectedImageIndex >= sharedFiles.Count)
            {
                Debug.LogError("共享图片索引无效");
                BackToMenu();
                return;
            }
            string fileName = sharedFiles[selectedImageIndex];
            string path = GameDataManager.GetSharedImagePath(fileName);
            if (!File.Exists(path))
            {
                Debug.LogError("共享图片文件不存在: " + path);
                BackToMenu();
                return;
            }
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            chosenSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            currentImageIndex = selectedImageIndex;
        }
        else
        {
            // 普通分类：从 AssetBundle 加载
            allSprites = AssetBundleManager.Instance.GetCategorySprites(selectedCategory);
            if (allSprites.Length == 0)
            {
                Debug.LogError("没有找到图片分类: " + selectedCategory);
                BackToMenu();
                return;
            }

            System.Array.Sort(allSprites, (a, b) => string.Compare(a.name, b.name));

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
        }

        Texture2D texture = chosenSprite.texture;

        // 上传分类和共享分类不支持收藏
        if (favoriteButton != null)
        {
            bool canFavorite = (selectedCategory != GameDataManager.UploadCategory &&
                                selectedCategory != GameDataManager.SharedCategory);
            favoriteButton.interactable = canFavorite;
        }
        UpdateFavoriteButtonColor();

        // ==================== 设置拼图区域 ====================
        rows = gridSize;
        cols = gridSize;
        totalPieces = rows * cols;

        float maxWidth = 1080f;
        float maxHeight = 700f;
        pieceSize = Mathf.Min(maxWidth / cols, maxHeight / rows);

        puzzleArea.anchorMin = new Vector2(0.5f, 0.5f);
        puzzleArea.anchorMax = new Vector2(0.5f, 0.5f);
        puzzleArea.pivot = new Vector2(0.5f, 0.5f);
        puzzleArea.sizeDelta = new Vector2(cols * pieceSize, rows * pieceSize);
        puzzleArea.anchoredPosition = new Vector2(0f, 300f);

        // 创建拼图内容容器
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

        // 每日拼图已完成的图片直接显示完整图
        if (isDailyPuzzle && GameDataManager.IsDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex))
        {
            GenerateCompletedPuzzle(texture);
            return;
        }

        // ==================== 切割并创建碎片 ====================
        Sprite[] pieces = CutTexture(texture, rows, cols);
        List<PuzzlePieceData> pieceDataList = new List<PuzzlePieceData>();
        for (int i = 0; i < totalPieces; i++)
            pieceDataList.Add(new PuzzlePieceData(i, pieces[i], Random.Range(0, 4) * 90));
        Shuffle(pieceDataList);

        // 设置碎片列表水平布局
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

        foreach (var data in pieceDataList)
        {
            CreateListPiece(data.sprite, data.index, data.rotation);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
        listContent.anchoredPosition = Vector2.zero;

        CreateGridOverlay();
    }

    /// <summary>
    /// 生成已完成拼图的完整图显示（用于每日拼图已完成的图片）。
    /// </summary>
    private void GenerateCompletedPuzzle(Texture2D texture)
    {
        victoryPanel.SetActive(false);
        if (rewardText != null) rewardText.text = "";

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

    /// <summary>
    /// 创建网格线覆盖层。
    /// </summary>
    private void CreateGridOverlay()
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
                new Vector2(gridLineThickness, totalHeight), new Vector2(x, 0f));
        }

        // 水平线
        for (int i = 0; i <= rows; i++)
        {
            float y = totalHeight / 2f - i * pieceSize;
            CreateGridLine(gridObj.transform, "HorizontalLine_" + i,
                new Vector2(totalWidth, gridLineThickness), new Vector2(0f, y));
        }
    }

    /// <summary>
    /// 创建单条网格线。
    /// </summary>
    private void CreateGridLine(Transform parent, string name, Vector2 size, Vector2 anchoredPos)
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
        img.raycastTarget = false;
    }

    #endregion

    #region 碎片创建与管理

    /// <summary>
    /// 在列表中创建一个碎片项（不可交互）。
    /// </summary>
    private void CreateListPiece(Sprite sprite, int index, int rotation)
    {
        GameObject pieceObj = Instantiate(piecePrefab, listContent);
        PuzzlePiece piece = pieceObj.GetComponent<PuzzlePiece>();
        piece.Initialize(sprite, index, rotation);
        piece.interactable = false;

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

    /// <summary>
    /// 点击列表中的碎片：将其移动到拼图区域，变为可交互。
    /// </summary>
    private void OnListPieceClicked(PuzzlePiece listPiece, GameObject listObj)
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

    /// <summary>
    /// 将未锁定的碎片返回到列表。
    /// </summary>
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

    #endregion

    #region 工具方法

    /// <summary>
    /// 将纹理切割为行列数的碎片 Sprite 数组。
    /// </summary>
    private Sprite[] CutTexture(Texture2D texture, int rows, int cols)
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

    /// <summary>
    /// 洗牌算法（Fisher-Yates）。
    /// </summary>
    private void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            T temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    #endregion

    #region 胜利结算

    /// <summary>
    /// 每次锁定碎片时调用，检查是否全部完成。
    /// </summary>
    public void CheckVictory()
    {
        lockedCount++;
        if (lockedCount >= totalPieces)
        {
            isVictory = true;

            // 播放胜利音效
            if (victorySound != null && SoundManager.Instance != null)
                SoundManager.Instance.PlayPuzzleSound(victorySound);

            if (isDailyPuzzle)
            {
                // 每日拼图逻辑
                GameDataManager.SetDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex, true);
                List<bool> flags = GameDataManager.GetDailyPuzzleCompletedFlags();
                int completedCount = 0;
                foreach (bool b in flags) if (b) completedCount++;

                if (completedCount >= GameDataManager.DailyPuzzleCount)
                {
                    // 全部每日拼图完成
                    if (dailyCompleteSound != null && SoundManager.Instance != null)
                        SoundManager.Instance.PlayPuzzleSound(dailyCompleteSound);

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
                    if (rewardText != null) rewardText.text = "本图已完成，可点击下一张/上一张继续";
                    victoryPanel.SetActive(true);
                }
            }
            else
            {
                // 普通模式
                GameDataManager.ConsumeStamina(GameDataManager.PuzzleStaminaCost);

                int baseReward = 0;
                int experienceReward = 0;
                switch (gridSize)
                {
                    case 2: baseReward = 5; experienceReward = 3; break;
                    case 8: baseReward = 10; experienceReward = 5; break;
                    case 10: baseReward = 15; experienceReward = 8; break;
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

    #endregion

    #region 提示功能

    /// <summary>
    /// 显示提示图（半透明原图）。
    /// </summary>
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

    /// <summary>
    /// 隐藏提示图。
    /// </summary>
    public void HideHint()
    {
        if (hintImage != null)
        {
            Destroy(hintImage.gameObject);
            hintImage = null;
        }
    }

    #endregion

    #region 图片切换

    /// <summary>
    /// 切换到下一张图片。
    /// </summary>
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

        // ===== 上传分类 =====
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            List<string> uploadFiles = GameDataManager.GetUploadedImages();
            if (uploadFiles.Count == 0) return;

            if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
            {
                ShowConfirm("体力不足，无法切换图片", null);
                return;
            }

            if (currentImageIndex >= uploadFiles.Count - 1)
            {
                ShowConfirm("已经到最后一张，是否直接到第一张？", () =>
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
            return;
        }

        // ===== 共享分类 =====
        if (selectedCategory == GameDataManager.SharedCategory)
        {
            List<string> sharedFiles = GameDataManager.GetSharedImages();
            if (sharedFiles.Count == 0) return;

            if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
            {
                ShowConfirm("体力不足，无法切换图片", null);
                return;
            }

            if (currentImageIndex >= sharedFiles.Count - 1)
            {
                ShowConfirm("已经到最后一张，是否直接到第一张？", () =>
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
            return;
        }

        // ===== 普通分类 =====
        if (allSprites == null || allSprites.Length == 0) return;
        if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            ShowConfirm("体力不足，无法切换图片", null);
            return;
        }

        // 从当前图片往后找第一张已解锁的图片
        int found = -1;
        for (int i = 1; i <= allSprites.Length; i++)
        {
            int idx = (currentImageIndex + i) % allSprites.Length;
            if (GameDataManager.IsImageUnlocked(selectedCategory, idx))
            {
                found = idx;
                break;
            }
        }

        if (found == -1)
        {
            ShowConfirm("没有其他已解锁的图片，请先解锁更多图片。", null);
            return;
        }

        if (found == currentImageIndex)
        {
            ShowConfirm("已经是最后一张已解锁的图片", null);
            return;
        }

        selectedImageIndex = found;
        StartNewGame();
    }

    /// <summary>
    /// 切换到上一张图片。
    /// </summary>
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

        // ===== 上传分类 =====
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            List<string> uploadFiles = GameDataManager.GetUploadedImages();
            if (uploadFiles.Count == 0) return;

            if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
            {
                ShowConfirm("体力不足，无法切换图片", null);
                return;
            }

            if (currentImageIndex <= 0)
            {
                ShowConfirm("已经到第一张，是否直接到最后一张？", () =>
                {
                    selectedImageIndex = uploadFiles.Count - 1;
                    StartNewGame();
                });
            }
            else
            {
                selectedImageIndex = currentImageIndex - 1;
                StartNewGame();
            }
            return;
        }

        // ===== 共享分类 =====
        if (selectedCategory == GameDataManager.SharedCategory)
        {
            List<string> sharedFiles = GameDataManager.GetSharedImages();
            if (sharedFiles.Count == 0) return;

            if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
            {
                ShowConfirm("体力不足，无法切换图片", null);
                return;
            }

            if (currentImageIndex <= 0)
            {
                ShowConfirm("已经到第一张，是否直接到最后一张？", () =>
                {
                    selectedImageIndex = sharedFiles.Count - 1;
                    StartNewGame();
                });
            }
            else
            {
                selectedImageIndex = currentImageIndex - 1;
                StartNewGame();
            }
            return;
        }

        // ===== 普通分类 =====
        if (allSprites == null || allSprites.Length == 0) return;
        if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            ShowConfirm("体力不足，无法切换图片", null);
            return;
        }

        // 从当前图片往前找第一张已解锁的图片
        int found = -1;
        for (int i = 1; i <= allSprites.Length; i++)
        {
            int idx = (currentImageIndex - i + allSprites.Length) % allSprites.Length;
            if (GameDataManager.IsImageUnlocked(selectedCategory, idx))
            {
                found = idx;
                break;
            }
        }

        if (found == -1)
        {
            ShowConfirm("没有其他已解锁的图片，请先解锁更多图片。", null);
            return;
        }

        if (found == currentImageIndex)
        {
            ShowConfirm("已经是第一张已解锁的图片", null);
            return;
        }

        selectedImageIndex = found;
        StartNewGame();
    }

    /// <summary>
    /// 加载每日拼图中指定索引的图片。
    /// </summary>
    private void LoadDailyPuzzleImage(int index)
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

    #endregion

    #region 确认弹窗

    private void ShowConfirm(string message, System.Action onConfirm)
    {
        confirmText.text = message;
        confirmAction = onConfirm;
        confirmPanel.SetActive(true);
    }

    private void OnConfirmYes()
    {
        confirmPanel.SetActive(false);
        confirmAction?.Invoke();
    }

    #endregion

    #region 收藏

    private void UpdateFavoriteButtonColor()
    {
        if (favoriteButton == null || selectedCategory == null || currentImageIndex < 0) return;

        Image buttonImage = favoriteButton.GetComponent<Image>();
        if (buttonImage == null) return;

        // 上传分类和共享分类：禁用收藏并保持白色
        if (selectedCategory == GameDataManager.UploadCategory ||
            selectedCategory == GameDataManager.SharedCategory)
        {
            favoriteButton.interactable = false;
            buttonImage.color = Color.white;
            return;
        }

        // 普通分类：启用并根据收藏状态变色
        favoriteButton.interactable = true;
        buttonImage.color = GameDataManager.IsFavorite(selectedCategory, currentImageIndex)
            ? new Color(1f, 0.84f, 0f, 1f)   // 金色
            : Color.white;
    }

    private void ToggleFavorite()
    {
        // 上传和共享分类不支持收藏
        if (selectedCategory == GameDataManager.UploadCategory ||
            selectedCategory == GameDataManager.SharedCategory) return;
        if (selectedCategory == null || currentImageIndex < 0) return;

        if (GameDataManager.IsFavorite(selectedCategory, currentImageIndex))
            GameDataManager.RemoveFavorite(selectedCategory, currentImageIndex);
        else
            GameDataManager.AddFavorite(selectedCategory, currentImageIndex);

        UpdateFavoriteButtonColor();
    }

    #endregion

    #region 触摸缩放与平移

    private void HandleTouchInput()
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

            // 缩放
            Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
            Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;
            float prevDistance = Vector2.Distance(touch0PrevPos, touch1PrevPos);
            float currentDistance = Vector2.Distance(touch0.position, touch1.position);

            if (prevDistance > 0.001f)
            {
                float zoomFactor = currentDistance / prevDistance;
                currentZoom = Mathf.Clamp(currentZoom * zoomFactor, minZoom, maxZoom);
            }

            // 平移
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

    private bool IsPointOverPuzzleArea(Vector2 screenPoint)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(puzzleArea, screenPoint, null);
    }

    private void ApplyContentTransform()
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

    #endregion

    #region 返回主菜单

    private void BackToMenu()
    {
        PlayerPrefs.SetInt("IsDailyPuzzle", 0);
        PlayerPrefs.Save();
        SceneManager.LoadScene("LevelScene");
    }

    #endregion
}

/// <summary>
/// 辅助数据结构：用于在生成碎片前临时保存碎片信息。
/// </summary>
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