using System.Collections;              // IEnumerator（协程）
using System.Collections.Generic;      // List<T>
using System.IO;                       // 文件操作（本脚本未直接使用，但保留以防扩展）
using UnityEngine;                     // Unity 基础 API
using UnityEngine.EventSystems;        // 事件系统（EventTrigger）
using UnityEngine.SceneManagement;     // SceneManager（场景切换）
using UnityEngine.UI;                  // UI 组件
using Random = UnityEngine.Random;     // 别名，避免和 System.Random 冲突

/// <summary>
/// 拼图游戏场景的主管理器。
///
/// 【这个脚本做什么？】
/// 这是 GameScene（拼图场景）的总指挥，负责：
///   1. 加载图片（从 AssetBundle 或本地文件）
///   2. 切割图片为 N×N 碎片
///   3. 创建碎片列表 + 拼图区域
///   4. 处理拖拽、旋转、吸附锁定
///   5. 判定胜利、结算奖励
///   6. 保存/恢复拼图进度
///   7. 每日拼图模式
///   8. 缩放、平移、提示、收藏
///
/// 【核心概念】
/// - 图片切成 rows×cols 个 Sprite
/// - 每个碎片是列表里的一个按钮，点击后进入拼图区
/// - 拼图区里的碎片支持拖拽和旋转，位置对且角度为 0 时锁定
///
/// 【进度保存】
/// - 每次锁定碎片后立即保存（非每日拼图）
/// - 下次进入同一张图、同一难度，已锁定的碎片直接摆好
/// - 全部完成自动清除进度
/// </summary>
public class GameManager : MonoBehaviour
{
    #region 单例

    /// <summary>
    /// 全局唯一实例。方便 PuzzlePiece 等脚本直接访问。
    /// </summary>
    public static GameManager Instance { get; private set; }

    #endregion

    #region UI 引用与配置

    [Header("音效")]
    public AudioClip victorySound;         // 胜利音效
    public AudioClip dailyCompleteSound;   // 每日拼图全部完成音效

    [Header("UI References")]
    public Button favoriteButton;          // 收藏按钮
    public Button nextImageButton;         // 下一张
    public Button prevImageButton;         // 上一张
    public Button backButton;              // 返回主菜单
    public Button hintButton;              // 提示按钮（按住显示原图）
    public Button returnButton;            // 把所有未锁定碎片退回列表
    public Button restartButton;           // 重新开始（清除进度）
    public RectTransform listContent;      // 碎片列表容器
    public RectTransform puzzleArea;       // 拼图区域
    public GameObject victoryPanel;        // 胜利面板
    public GameObject piecePrefab;         // 碎片预制体
    public Text rewardText;                // 奖励文字

    [Header("确认弹窗")]
    public GameObject confirmPanel;
    public Text confirmText;
    public Button confirmYesButton;
    public Button confirmNoButton;

    [Header("Game Flow")]
    public float easyTimeLimit = 120f;     // 简单难度时间限制
    public float normalTimeLimit = 240f;   // 普通难度时间限制
    public float hardTimeLimit = 360f;     // 困难难度时间限制

    [Header("List Piece Settings")]
    public float listPieceSize = 200f;     // 列表里碎片显示的尺寸

    [Header("Grid Settings")]
    public Color gridLineColor = new Color(0f, 0f, 0f, 0.5f);  // 网格线颜色
    public float gridLineThickness = 2f;                        // 网格线粗细

    [Header("Puzzle Settings")]
    public float pieceSize = 100f;         // 拼图区每块碎片的尺寸（运行时计算）
    public float spacingFactor = 1f;       // 间距系数（预留）

    #endregion

    #region 对外属性

    /// <summary>已锁定的碎片数量（供 PuzzlePiece 查询）。</summary>
    public int LockedCount => lockedCount;

    /// <summary>总碎片数。</summary>
    public int TotalPieces => totalPieces;

    /// <summary>是否处于多指触摸状态（防止拖拽误触）。</summary>
    public bool IsMultiTouch => isMultiTouch;

    #endregion

    #region 私有状态

    private bool isDailyPuzzle = false;         // 是否每日拼图模式
    private int currentImageIndex = -1;         // 当前图片的索引（用于存档）
    private string selectedCategory;            // 当前分类
    private int selectedImageIndex = -1;        // 当前图片在分类里的索引
    private int gridSize = 2;                   // 难度（2/8/10）
    private float timeRemaining;                // 剩余时间
    private bool isVictory = false;             // 是否已胜利（停止倒计时）

    private RectTransform puzzleContent;        // 拼图内容容器（所有碎片和网格的父物体）
    private float currentZoom = 1f;             // 当前缩放
    private Vector2 contentOffset;              // 当前平移量
    private float maxZoom = 2f;                 // 最大缩放
    private float minZoom = 1f;                 // 最小缩放

    private Sprite[] allSprites;                // 当前分类的所有 Sprite
    private Sprite chosenSprite;                // 当前使用的图片
    private int rows, cols;                     // 行数、列数
    private List<PuzzlePiece> activePieces = new List<PuzzlePiece>();  // 拼图区里所有激活的碎片
    private int totalPieces;                    // 总碎片数
    private int lockedCount = 0;                // 已锁定数量

    private Image hintImage;                    // 提示图（半透明原图）
    private bool isMultiTouch = false;          // 多指状态标记

    private System.Action confirmAction;        // 确认弹窗的回调
    private int dailyPuzzleCurrentIndex = -1;   // 每日拼图当前索引

    /// <summary>防重入标记：正在加载时忽略新的加载请求。</summary>
    private bool isLoading = false;

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake 在物体创建时立即调用。
    /// 这里做初始化：单例、按钮事件绑定、提示按钮的按下/抬起。
    /// </summary>
    private void Awake()
    {
        Instance = this;

        // ---------- 绑定基础按钮 ----------
        favoriteButton.onClick.AddListener(ToggleFavorite);
        nextImageButton.onClick.AddListener(NextImage);
        prevImageButton.onClick.AddListener(PrevImage);
        backButton.onClick.AddListener(BackToMenu);
        returnButton.onClick.AddListener(ReturnUnlockedPieces);

        // 重新开始按钮（如果存在）
        if (restartButton != null)
            restartButton.onClick.AddListener(OnRestartClicked);

        // ---------- 提示按钮：按住显示原图，抬起隐藏 ----------
        // 用 EventTrigger 而不是 onClick，因为我们需要"按住"这种持续状态
        EventTrigger trigger = hintButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = hintButton.gameObject.AddComponent<EventTrigger>();

        // 按下：显示提示图
        EventTrigger.Entry pointerDownEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDownEntry.callback.AddListener((data) => { ShowHint(); });
        trigger.triggers.Add(pointerDownEntry);

        // 抬起：隐藏提示图
        EventTrigger.Entry pointerUpEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUpEntry.callback.AddListener((data) => { HideHint(); });
        trigger.triggers.Add(pointerUpEntry);

        // ---------- 确认弹窗 ----------
        confirmYesButton.onClick.AddListener(OnConfirmYes);
        confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);

        // ---------- 拼图区域加裁剪 ----------
        // RectMask2D 让超出区域的部分不显示
        // 这样碎片即使暂时被拖到边界外，也会被"裁掉"，看起来更整洁
        if (puzzleArea.GetComponent<RectMask2D>() == null)
            puzzleArea.gameObject.AddComponent<RectMask2D>();

        // 胜利面板默认隐藏
        victoryPanel.SetActive(false);
    }

    /// <summary>
    /// Start 在物体第一帧启用时调用。
    /// 这里读取玩家选中的图片、难度，并启动拼图构建。
    /// </summary>
    private void Start()
    {
        // 判断是否每日拼图模式（由 MainMenuManager 写入 PlayerPrefs）
        isDailyPuzzle = PlayerPrefs.GetInt("IsDailyPuzzle", 0) == 1;

        if (isDailyPuzzle)
        {
            // ---------- 每日拼图模式 ----------
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

            // 图片格式 "分类_索引"，用 '_' 拆开
            string[] parts = images[dailyPuzzleCurrentIndex].Split('_');
            selectedCategory = parts[0];
            selectedImageIndex = int.Parse(parts[1]);
            gridSize = difficulties[dailyPuzzleCurrentIndex];
        }
        else
        {
            // ---------- 普通模式 ----------
            // 这些值由 MainMenuManager 在切场景前写入 PlayerPrefs
            selectedCategory = PlayerPrefs.GetString("SelectedCategory", "1");
            gridSize = PlayerPrefs.GetInt("Difficulty", 6);
            selectedImageIndex = PlayerPrefs.GetInt("SelectedImageIndex", -1);
        }

        // 根据难度设置时间
        switch (gridSize)
        {
            case 2: timeRemaining = easyTimeLimit; break;
            case 8: timeRemaining = normalTimeLimit; break;
            case 10: timeRemaining = hardTimeLimit; break;
            default: timeRemaining = easyTimeLimit; break;
        }

        // 启动异步加载
        StartCoroutine(StartNewGameAsync());
    }

    /// <summary>
    /// Update 每帧调用。
    /// 倒计时 + 触摸缩放/平移。
    /// </summary>
    private void Update()
    {
        // 倒计时（胜利后停止）
        if (!isVictory)
        {
            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0) timeRemaining = 0;
        }

        // 处理双指缩放、平移
        HandleTouchInput();
    }

    #endregion

    #region 游戏初始化

    /// <summary>
    /// 异步开始新拼图（防重入 + 释放旧纹理）。
    ///
    /// 【为什么要异步？】
    /// 上传/共享分类的图片需要从文件读 + 解码，可能耗时几十到几百毫秒。
    /// 如果同步，UI 会卡住。用协程让出主线程，保持流畅。
    /// </summary>
    private IEnumerator StartNewGameAsync()
    {
        // 防重入：如果上一次还在加载，忽略本次请求
        if (isLoading)
        {
            Debug.Log("正在加载中，忽略本次请求");
            yield break;
        }
        isLoading = true;

        // 释放上一次的上传/共享图片纹理（AB 来源的不用释放）
        ReleaseChosenSprite();

        // 加载并构建拼图
        yield return LoadAndBuildPuzzle();

        isLoading = false;
    }

    /// <summary>
    /// 实际加载逻辑：读图片 → 选图片 → 构建拼图。
    /// </summary>
    private IEnumerator LoadAndBuildPuzzle()
    {
        // ---------- 体力检查（每日拼图不消耗体力） ----------
        if (!isDailyPuzzle && GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            Debug.Log("体力不足，无法开始拼图");
            BackToMenu();
            yield break;
        }

        // ---------- 重置状态 ----------
        isVictory = false;
        victoryPanel.SetActive(false);
        if (rewardText != null) rewardText.text = "";

        // 清理提示图
        if (hintImage != null) { Destroy(hintImage.gameObject); hintImage = null; }

        // 清空碎片列表
        foreach (Transform child in listContent) Destroy(child.gameObject);

        // 清理拼图区内容
        if (puzzleContent != null) { Destroy(puzzleContent.gameObject); puzzleContent = null; }

        activePieces.Clear();
        lockedCount = 0;

        // ==================== 加载图片 ====================
        Sprite loadedSprite = null;

        if (selectedCategory == GameDataManager.UploadCategory)
        {
            // ---------- 上传分类：从文件异步加载 ----------
            List<string> uploadFiles = GameDataManager.GetUploadedImages();

            // 索引越界保护
            if (selectedImageIndex < 0 || selectedImageIndex >= uploadFiles.Count)
            {
                Debug.LogError("上传图片索引无效");
                BackToMenu();
                yield break;
            }

            string path = GameDataManager.GetUploadedImagePath(uploadFiles[selectedImageIndex]);
            // yield return 等待异步加载（后台线程解码，主线程创建 Sprite）
            yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => loadedSprite = s);

            if (loadedSprite == null)
            {
                Debug.LogError($"加载上传图片失败: {path}");
                BackToMenu();
                yield break;
            }

            chosenSprite = loadedSprite;
            currentImageIndex = selectedImageIndex;
        }
        else if (selectedCategory == GameDataManager.SharedCategory)
        {
            // ---------- 共享分类：同上传，路径不同 ----------
            List<string> sharedFiles = GameDataManager.GetSharedImages();

            if (selectedImageIndex < 0 || selectedImageIndex >= sharedFiles.Count)
            {
                Debug.LogError("共享图片索引无效");
                BackToMenu();
                yield break;
            }

            string path = GameDataManager.GetSharedImagePath(sharedFiles[selectedImageIndex]);
            yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => loadedSprite = s);

            if (loadedSprite == null)
            {
                Debug.LogError($"加载共享图片失败: {path}");
                BackToMenu();
                yield break;
            }

            chosenSprite = loadedSprite;
            currentImageIndex = selectedImageIndex;
        }
        else
        {
            // ---------- 普通分类：从 AssetBundle 同步获取 ----------
            allSprites = AssetBundleManager.Instance.GetCategorySprites(selectedCategory);

            if (allSprites.Length == 0)
            {
                Debug.LogError("没有找到图片分类: " + selectedCategory);
                BackToMenu();
                yield break;
            }

            // 按名字排序，保证顺序稳定
            System.Array.Sort(allSprites, (a, b) => string.Compare(a.name, b.name));

            // 选图片
            if (selectedImageIndex >= 0 && selectedImageIndex < allSprites.Length)
            {
                chosenSprite = allSprites[selectedImageIndex];
                currentImageIndex = selectedImageIndex;
            }
            else
            {
                // 无效索引 → 随机选一张
                currentImageIndex = Random.Range(0, allSprites.Length);
                chosenSprite = allSprites[currentImageIndex];
                selectedImageIndex = currentImageIndex;
            }
        }

        // 图片已就绪，构建拼图
        BuildPuzzle();
    }

    /// <summary>
    /// 释放上一次的上传/共享图片纹理。
    ///
    /// 【关键点】
    /// - 上传/共享分类的 Sprite 是运行时 Sprite.Create 出来的，
    ///   必须手动 Destroy，否则显存泄漏。
    /// - AssetBundle 来源的 Sprite 由 AssetBundleManager 统一管理，
    ///   不能在这里 Destroy，否则其他地方引用会出问题。
    /// </summary>
    private void ReleaseChosenSprite()
    {
        if (chosenSprite == null) return;

        bool isFileBased = (selectedCategory == GameDataManager.UploadCategory ||
                            selectedCategory == GameDataManager.SharedCategory);

        if (isFileBased)
        {
            // 先销毁 Texture，再销毁 Sprite（顺序很重要）
            if (chosenSprite.texture != null)
                Destroy(chosenSprite.texture);

            Destroy(chosenSprite);
        }

        chosenSprite = null;
    }

    /// <summary>
    /// 构建拼图：切割图片、读进度、创建碎片和网格。
    /// </summary>
    private void BuildPuzzle()
    {
        Texture2D texture = chosenSprite.texture;

        // ---------- 收藏按钮状态 ----------
        // 上传/共享分类不支持收藏
        if (favoriteButton != null)
        {
            bool canFavorite = (selectedCategory != GameDataManager.UploadCategory &&
                                selectedCategory != GameDataManager.SharedCategory);
            favoriteButton.interactable = canFavorite;
        }
        UpdateFavoriteButtonColor();

        // ---------- 设置拼图区域 ----------
        rows = gridSize;
        cols = gridSize;
        totalPieces = rows * cols;

        // 每块碎片的尺寸：让整个拼图不超过 1080×700
        float maxWidth = 1080f;
        float maxHeight = 700f;
        pieceSize = Mathf.Min(maxWidth / cols, maxHeight / rows);

        // 锚点居中
        puzzleArea.anchorMin = new Vector2(0.5f, 0.5f);
        puzzleArea.anchorMax = new Vector2(0.5f, 0.5f);
        puzzleArea.pivot = new Vector2(0.5f, 0.5f);
        puzzleArea.sizeDelta = new Vector2(cols * pieceSize, rows * pieceSize);
        puzzleArea.anchoredPosition = new Vector2(0f, 300f);

        // ---------- 创建拼图内容容器 ----------
        // 所有碎片和网格都挂在这个容器下
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

        // ---------- 每日拼图已完成的图片直接显示整图 ----------
        if (isDailyPuzzle && GameDataManager.IsDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex))
        {
            GenerateCompletedPuzzle(texture);
            return;
        }

        // ---------- 切割纹理 ----------
        Sprite[] pieces = CutTexture(texture, rows, cols);

        // ---------- 读取进度 ----------
        bool[] lockedFlags = null;
        if (!isDailyPuzzle)
        {
            lockedFlags = GameDataManager.LoadPuzzleProgress(
                selectedCategory, currentImageIndex, gridSize, totalPieces);

            if (lockedFlags != null)
            {
                int lockedNum = 0;
                for (int i = 0; i < lockedFlags.Length; i++) if (lockedFlags[i]) lockedNum++;
                Debug.Log($"[进度] 读取到 {selectedCategory}_{currentImageIndex}_{gridSize} 的进度：{lockedNum}/{totalPieces} 已锁定");
            }
        }

        // ---------- 分两类：已锁定 / 未锁定 ----------
        List<int> lockedIndices = new List<int>();
        List<int> unlockedIndices = new List<int>();
        for (int i = 0; i < totalPieces; i++)
        {
            if (lockedFlags != null && lockedFlags[i])
                lockedIndices.Add(i);
            else
                unlockedIndices.Add(i);
        }

        // ---------- 设置碎片列表布局 ----------
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

        // 自适应宽度
        ContentSizeFitter fitter = listContent.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        // ---------- 创建已锁定碎片（直接摆在正确位置） ----------
        foreach (int idx in lockedIndices)
        {
            CreateLockedPieceFromProgress(pieces[idx], idx);
        }

        // ---------- 创建未锁定碎片（放回列表，随机旋转） ----------
        List<PuzzlePieceData> pieceDataList = new List<PuzzlePieceData>();
        for (int i = 0; i < unlockedIndices.Count; i++)
        {
            int idx = unlockedIndices[i];
            // 随机 0/90/180/270 度
            pieceDataList.Add(new PuzzlePieceData(idx, pieces[idx], Random.Range(0, 4) * 90));
        }
        // 洗牌，让列表顺序随机
        Shuffle(pieceDataList);

        foreach (var data in pieceDataList)
        {
            CreateListPiece(data.sprite, data.index, data.rotation);
        }

        // 强制重建布局
        LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
        listContent.anchoredPosition = Vector2.zero;

        // 创建网格覆盖层
        CreateGridOverlay();
    }

    /// <summary>
    /// 每日拼图中已完成的图片直接显示完整图（无需拼）。
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
        img.raycastTarget = false;   // 不接受点击
    }

    /// <summary>
    /// 创建网格线覆盖层（显示拼图槽位的分隔线）。
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

        // 垂直线（cols+1 条，包括左右边界）
        for (int i = 0; i <= cols; i++)
        {
            float x = -totalWidth / 2f + i * pieceSize;
            CreateGridLine(gridObj.transform, "VerticalLine_" + i,
                new Vector2(gridLineThickness, totalHeight), new Vector2(x, 0f));
        }

        // 水平线（rows+1 条）
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
        img.raycastTarget = false;   // 网格线不拦截点击
    }

    #endregion

    #region 碎片创建与管理

    /// <summary>
    /// 计算指定碎片编号对应的正确位置（拼图区本地坐标）。
    ///
    /// 【坐标计算】
    /// 碎片编号 index 从 0 开始，按从左到右、从上到下编号。
    ///   row = index / cols（第几行）
    ///   col = index % cols（第几列）
    ///
    /// 目标位置 = 相对于拼图区中心的偏移。
    /// 例：4×4 拼图，第 0 号碎片（左上角）的位置 = 最左上角。
    /// </summary>
    private Vector2 GetTargetPositionForPiece(int pieceIndex)
    {
        int row = pieceIndex / cols;
        int col = pieceIndex % cols;

        // 例：cols=4 时，col=0 → (0 - 1.5) * pieceSize = -1.5*pieceSize
        //     col=3 → (3 - 1.5) * pieceSize = +1.5*pieceSize
        float targetX = (col - (cols - 1) / 2f) * pieceSize;
        float targetY = ((rows - 1) / 2f - row) * pieceSize;

        return new Vector2(targetX, targetY);
    }

    /// <summary>
    /// 从存档进度直接创建一个已锁定的碎片。
    /// 特点是：直接摆在正确位置、粉色、不可交互、不计入列表。
    /// </summary>
    private void CreateLockedPieceFromProgress(Sprite sprite, int pieceIndex)
    {
        GameObject pieceObj = Instantiate(piecePrefab, puzzleContent);
        PuzzlePiece piece = pieceObj.GetComponent<PuzzlePiece>();
        piece.Initialize(sprite, pieceIndex, 0);

        // 直接标记为已锁定、不可交互
        piece.isLocked = true;
        piece.interactable = false;

        // 设置位置和尺寸
        RectTransform rt = pieceObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(pieceSize, pieceSize);
        rt.anchoredPosition = GetTargetPositionForPiece(pieceIndex);

        // 移除按钮组件（已锁定不需要点击）
        Button btn = pieceObj.GetComponent<Button>();
        if (btn != null) Destroy(btn);

        // 设置"已锁定"的淡粉色
        Image img = pieceObj.GetComponent<Image>();
        if (img != null) img.color = new Color(1f, 0.8f, 0.8f, 1f);

        activePieces.Add(piece);
        lockedCount++;
    }

    /// <summary>
    /// 在碎片列表里创建一个碎片（不可交互，点击后才进拼图区）。
    /// </summary>
    private void CreateListPiece(Sprite sprite, int index, int rotation)
    {
        GameObject pieceObj = Instantiate(piecePrefab, listContent);
        PuzzlePiece piece = pieceObj.GetComponent<PuzzlePiece>();
        piece.Initialize(sprite, index, rotation);
        piece.interactable = false;

        RectTransform rt = pieceObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(listPieceSize, listPieceSize);

        // 用 LayoutElement 固定尺寸，避免被 HorizontalLayoutGroup 拉伸
        LayoutElement layoutElement = pieceObj.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = listPieceSize;
        layoutElement.preferredHeight = listPieceSize;
        layoutElement.minWidth = listPieceSize;
        layoutElement.minHeight = listPieceSize;
        layoutElement.flexibleWidth = 0;
        layoutElement.flexibleHeight = 0;

        // 加 Button（点击把碎片移到拼图区）
        if (pieceObj.GetComponent<Button>() == null) pieceObj.AddComponent<Button>();
        pieceObj.GetComponent<Button>().onClick.AddListener(() => OnListPieceClicked(piece, pieceObj));
    }

    /// <summary>
    /// 点击列表中的碎片：把碎片从列表移到拼图区。
    /// 新碎片随机出现在拼图区内，可拖拽、可旋转。
    /// </summary>
    private void OnListPieceClicked(PuzzlePiece listPiece, GameObject listObj)
    {
        // ---------- 在拼图区创建新碎片 ----------
        GameObject newPieceObj = Instantiate(piecePrefab, puzzleContent);
        PuzzlePiece newPiece = newPieceObj.GetComponent<PuzzlePiece>();
        newPiece.Initialize(listPiece.GetComponent<Image>().sprite, listPiece.pieceIndex, listPiece.currentRotation);
        newPiece.interactable = true;

        // 计算目标位置（吸附用）
        newPiece.targetPosition = GetTargetPositionForPiece(listPiece.pieceIndex);

        // 尺寸
        RectTransform rt = newPieceObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(pieceSize, pieceSize);

        // 随机初始位置（在拼图区范围内）
        float halfW = puzzleArea.rect.width / 2f - pieceSize / 2f;
        float halfH = puzzleArea.rect.height / 2f - pieceSize / 2f;
        rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));

        // 移除按钮组件（拖拽和 Button 会冲突）
        Button btn = newPieceObj.GetComponent<Button>();
        if (btn != null) Destroy(btn);

        // 边界保护
        newPiece.ClampPositionToPuzzleArea();

        activePieces.Add(newPiece);
        Destroy(listObj);   // 销毁列表中的原碎片
    }

    /// <summary>
    /// 把所有"未锁定"的碎片退回列表（重新打乱）。
    /// 已锁定的碎片保持不动。
    /// </summary>
    public void ReturnUnlockedPieces()
    {
        // 先收集要退回的碎片
        List<PuzzlePiece> unlockedPieces = new List<PuzzlePiece>();
        foreach (var piece in activePieces)
        {
            if (!piece.isLocked) unlockedPieces.Add(piece);
        }

        // 逐个退回
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

    /// <summary>
    /// 点击"重新开始"：清进度 + 所有碎片（含已锁定）退回列表。
    /// </summary>
    private void OnRestartClicked()
    {
        if (isDailyPuzzle)
        {
            ShowConfirm("每日拼图不支持重新开始", null);
            return;
        }

        ShowConfirm("是否重新开始？所有已锁定的碎片将返回碎片列表。", () =>
        {
            Debug.Log("[重新开始] 用户确认");

            // 清除进度存档
            GameDataManager.ClearPuzzleProgress(selectedCategory, currentImageIndex, gridSize);
            Debug.Log($"[重新开始] 已清除 {selectedCategory}_{currentImageIndex}_{gridSize} 的进度");

            // 重建拼图（从空进度开始）
            StartCoroutine(StartNewGameAsync());
        });
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 把纹理切割成 rows×cols 个 Sprite。
    ///
    /// 【坐标系陷阱】
    /// Unity 的 Texture2D 原点在"左下角"，而 Sprite 我们希望
    /// 碎片编号从"左上角"开始。
    /// 所以 rows 遍历时用 (rows - 1 - r) 翻转 Y。
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
                // rect 的 y 从底部开始，所以要翻转
                Rect rect = new Rect(c * pieceWidth, (rows - 1 - r) * pieceHeight, pieceWidth, pieceHeight);
                sprites[r * cols + c] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
            }
        }
        return sprites;
    }

    /// <summary>
    /// Fisher-Yates 洗牌算法。
    /// 
    /// 【为什么用它？】
    /// 简单、均匀、O(n) 时间。
    /// 核心思想：从前往后遍历，每次和后面随机一个位置交换。
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
    /// 每次锁定碎片时调用（由 PuzzlePiece 触发）。
    /// 检查是否全部完成，并处理奖励。
    /// </summary>
    public void CheckVictory()
    {
        lockedCount++;

        // ★ 每次锁定后立即保存进度（非每日拼图）
        if (!isDailyPuzzle)
        {
            SaveCurrentProgress();
        }

        if (lockedCount >= totalPieces)
        {
            // ==================== 胜利！ ====================
            isVictory = true;

            // 清除进度存档（已完成）
            if (!isDailyPuzzle)
            {
                GameDataManager.ClearPuzzleProgress(selectedCategory, currentImageIndex, gridSize);
                Debug.Log($"[进度] 拼图完成，已清除 {selectedCategory}_{currentImageIndex}_{gridSize} 的进度");
            }

            // 播放胜利音效
            if (victorySound != null && SoundManager.Instance != null)
                SoundManager.Instance.PlayPuzzleSound(victorySound);

            if (isDailyPuzzle)
            {
                // ---------- 每日拼图模式 ----------
                GameDataManager.SetDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex, true);

                // 统计已完成数量
                List<bool> flags = GameDataManager.GetDailyPuzzleCompletedFlags();
                int completedCount = 0;
                foreach (bool b in flags) if (b) completedCount++;

                if (completedCount >= GameDataManager.DailyPuzzleCount)
                {
                    // 全部 10 张都完成，发放总奖励
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
                    // 还有别的图要拼
                    if (rewardText != null) rewardText.text = "本图已完成，可点击下一张/上一张继续";
                    victoryPanel.SetActive(true);
                }
            }
            else
            {
                // ---------- 普通模式 ----------
                // 消耗体力
                GameDataManager.ConsumeStamina(GameDataManager.PuzzleStaminaCost);

                // 根据难度计算基础奖励
                int baseReward = 0;
                int experienceReward = 0;
                switch (gridSize)
                {
                    case 2: baseReward = 5; experienceReward = 3; break;
                    case 8: baseReward = 10; experienceReward = 5; break;
                    case 10: baseReward = 15; experienceReward = 8; break;
                }

                // 限时奖励（时间没到就完成）
                int bonus = (timeRemaining > 0) ? 3 : 0;
                int totalReward = baseReward + bonus;

                // 是否首次完成（首通有金币，之后只有经验）
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

    /// <summary>
    /// 把当前已锁定碎片的状态保存到 PlayerPrefs。
    ///
    /// 【核心流程】
    /// 1. 建一个长度 = totalPieces 的 bool 数组（全 false）
    /// 2. 遍历 activePieces，把已锁定碎片的对应位置设为 true
    /// 3. 交给 GameDataManager 转成"位串"存储
    /// </summary>
    private void SaveCurrentProgress()
    {
        if (totalPieces <= 0) return;

        bool[] flags = new bool[totalPieces];
        foreach (var piece in activePieces)
        {
            if (piece == null || !piece.isLocked) continue;
            int idx = piece.pieceIndex;
            if (idx >= 0 && idx < totalPieces)
                flags[idx] = true;
        }
        GameDataManager.SavePuzzleProgress(selectedCategory, currentImageIndex, gridSize, flags);
    }

    #endregion

    #region 提示功能

    /// <summary>
    /// 显示提示图：在拼图区上覆盖一层半透明原图。
    /// </summary>
    public void ShowHint()
    {
        if (hintImage != null) return;   // 已经显示就不重复创建

        GameObject hintObj = new GameObject("HintImage", typeof(RectTransform));
        hintObj.transform.SetParent(puzzleArea, false);

        // 放到最底层，不遮挡碎片
        hintObj.transform.SetAsFirstSibling();

        RectTransform hintRect = hintObj.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.5f, 0.5f);
        hintRect.anchorMax = new Vector2(0.5f, 0.5f);
        hintRect.pivot = new Vector2(0.5f, 0.5f);
        hintRect.sizeDelta = puzzleArea.sizeDelta;
        hintRect.anchoredPosition = Vector2.zero;

        Image img = hintObj.AddComponent<Image>();
        img.sprite = chosenSprite;
        img.color = new Color(1f, 1f, 1f, 0.3f);   // 30% 透明度
        img.raycastTarget = false;                  // 不拦截点击

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
    /// 不同分类的切换逻辑不同（上传/共享/普通）。
    /// </summary>
    public void NextImage()
    {
        // ★ 切换前保存当前进度
        if (!isDailyPuzzle && !isVictory)
            SaveCurrentProgress();

        if (isDailyPuzzle)
        {
            // 每日拼图：循环切换
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
                // 已经是最后一张，问是否回第一张
                ShowConfirm("已经到最后一张，是否直接到第一张？", () =>
                {
                    selectedImageIndex = 0;
                    StartCoroutine(StartNewGameAsync());
                });
            }
            else
            {
                selectedImageIndex = currentImageIndex + 1;
                StartCoroutine(StartNewGameAsync());
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
                    StartCoroutine(StartNewGameAsync());
                });
            }
            else
            {
                selectedImageIndex = currentImageIndex + 1;
                StartCoroutine(StartNewGameAsync());
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

        // 从当前位置往后找第一张"已解锁"的图片
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
        StartCoroutine(StartNewGameAsync());
    }

    /// <summary>
    /// 切换到上一张图片（逻辑与 NextImage 对称）。
    /// </summary>
    public void PrevImage()
    {
        // ★ 切换前保存当前进度
        if (!isDailyPuzzle && !isVictory)
            SaveCurrentProgress();

        if (isDailyPuzzle)
        {
            int count = GameDataManager.GetDailyPuzzleImages().Count;
            if (count == 0) return;
            // 加 count 防止负数
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
                    StartCoroutine(StartNewGameAsync());
                });
            }
            else
            {
                selectedImageIndex = currentImageIndex - 1;
                StartCoroutine(StartNewGameAsync());
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
                    StartCoroutine(StartNewGameAsync());
                });
            }
            else
            {
                selectedImageIndex = currentImageIndex - 1;
                StartCoroutine(StartNewGameAsync());
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

        // 往前找第一张已解锁的图片
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
        StartCoroutine(StartNewGameAsync());
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

        StartCoroutine(StartNewGameAsync());
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

    /// <summary>
    /// 刷新收藏按钮的颜色：
    /// - 已收藏 → 金色
    /// - 未收藏 → 白色
    /// - 上传/共享分类 → 禁用 + 白色
    /// </summary>
    private void UpdateFavoriteButtonColor()
    {
        if (favoriteButton == null || selectedCategory == null || currentImageIndex < 0) return;

        Image buttonImage = favoriteButton.GetComponent<Image>();
        if (buttonImage == null) return;

        // 上传和共享分类不能收藏
        if (selectedCategory == GameDataManager.UploadCategory ||
            selectedCategory == GameDataManager.SharedCategory)
        {
            favoriteButton.interactable = false;
            buttonImage.color = Color.white;
            return;
        }

        // 普通分类根据收藏状态变色
        favoriteButton.interactable = true;
        buttonImage.color = GameDataManager.IsFavorite(selectedCategory, currentImageIndex)
            ? new Color(1f, 0.84f, 0f, 1f)   // 金色
            : Color.white;
    }

    /// <summary>
    /// 切换收藏状态。
    /// </summary>
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

    /// <summary>
    /// 处理双指缩放和平移。
    ///
    /// 【为什么要检测"多点触摸"？】
    /// 单个手指在拖拽碎片时，不应该触发缩放/平移。
    /// 只有两个手指同时按在拼图区，才算是缩放操作。
    /// </summary>
    private void HandleTouchInput()
    {
        if (Input.touchCount == 1)
        {
            // 单指：不是缩放状态
            isMultiTouch = false;
        }
        else if (Input.touchCount >= 2)
        {
            // 双指及以上：进入缩放状态
            isMultiTouch = true;

            Touch touch0 = Input.GetTouch(0);
            Touch touch1 = Input.GetTouch(1);

            // 两根手指都必须按在拼图区内才处理
            if (!IsPointOverPuzzleArea(touch0.position) || !IsPointOverPuzzleArea(touch1.position))
                return;

            // ---------- 缩放 ----------
            // 计算两指距离的变化比例
            Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
            Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

            float prevDistance = Vector2.Distance(touch0PrevPos, touch1PrevPos);
            float currentDistance = Vector2.Distance(touch0.position, touch1.position);

            if (prevDistance > 0.001f)   // 防止除零
            {
                float zoomFactor = currentDistance / prevDistance;
                currentZoom = Mathf.Clamp(currentZoom * zoomFactor, minZoom, maxZoom);
            }

            // ---------- 平移 ----------
            // 计算两指中心点的移动
            Vector2 prevMidpoint = (touch0PrevPos + touch1PrevPos) / 2f;
            Vector2 currentMidpoint = (touch0.position + touch1.position) / 2f;

            // 把屏幕坐标转换到拼图区的本地坐标
            Vector2 localCurrent, localPrev;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(puzzleArea, currentMidpoint, null, out localCurrent);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(puzzleArea, prevMidpoint, null, out localPrev);
            Vector2 localDelta = localCurrent - localPrev;

            contentOffset += localDelta;

            // 应用变换
            ApplyContentTransform();
        }
        else
        {
            isMultiTouch = false;
        }
    }

    /// <summary>
    /// 判断屏幕点是否在拼图区域内。
    /// </summary>
    private bool IsPointOverPuzzleArea(Vector2 screenPoint)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(puzzleArea, screenPoint, null);
    }

    /// <summary>
    /// 应用缩放和平移。
    /// 平移量会被限制在合理范围内（防止把图拖出屏幕）。
    /// </summary>
    private void ApplyContentTransform()
    {
        if (puzzleContent == null) return;

        // 缩放后的内容尺寸
        float contentWidth = puzzleArea.sizeDelta.x * currentZoom;
        float contentHeight = puzzleArea.sizeDelta.y * currentZoom;

        // 最大允许的偏移量
        float maxOffsetX = Mathf.Max(0, (contentWidth - puzzleArea.sizeDelta.x) / 2f);
        float maxOffsetY = Mathf.Max(0, (contentHeight - puzzleArea.sizeDelta.y) / 2f);

        // 限制偏移
        contentOffset.x = Mathf.Clamp(contentOffset.x, -maxOffsetX, maxOffsetX);
        contentOffset.y = Mathf.Clamp(contentOffset.y, -maxOffsetY, maxOffsetY);

        // 应用
        puzzleContent.localScale = new Vector3(currentZoom, currentZoom, 1f);
        puzzleContent.anchoredPosition = contentOffset;
    }

    #endregion

    #region 返回主菜单

    /// <summary>
    /// 返回主菜单（LevelScene）。
    /// 离开前保存进度、释放纹理。
    /// </summary>
    private void BackToMenu()
    {
        // ★ 返回前保存进度（如果没胜利）
        if (!isDailyPuzzle && !isVictory)
            SaveCurrentProgress();

        ReleaseChosenSprite();

        // 清掉每日拼图标记，避免下次进游戏误判
        PlayerPrefs.SetInt("IsDailyPuzzle", 0);
        PlayerPrefs.Save();
        SceneManager.LoadScene("LevelScene");
    }

    #endregion
}

/// <summary>
/// 辅助数据结构：在生成碎片前临时保存碎片信息。
/// 
/// 【为什么要它？】
/// 在"先生成数据列表 → 洗牌 → 再创建 UI"的流程里，
/// 需要一个中间载体来承载 (索引, 精灵, 旋转角) 三件套。
/// </summary>
public class PuzzlePieceData
{
    public int index;        // 碎片编号
    public Sprite sprite;    // 碎片图片
    public int rotation;     // 初始旋转（0/90/180/270）

    public PuzzlePieceData(int index, Sprite sprite, int rotation)
    {
        this.index = index;
        this.sprite = sprite;
        this.rotation = rotation;
    }
}