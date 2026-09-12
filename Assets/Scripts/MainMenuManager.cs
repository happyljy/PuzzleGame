using System;                              // Action 委托、DateTime、Exception 等基础类型
using System.Collections;                  // IEnumerator（协程返回类型）
using System.Collections.Generic;          // List、Dictionary、HashSet 等泛型集合
using System.IO;                           // File、Directory、Path（文件操作）
using System.Text.RegularExpressions;      // Regex（正则表达式，用于验证玩家名字格式）
using UnityEngine;                         // Unity 基础 API
using UnityEngine.SceneManagement;         // SceneManager（场景切换）
using UnityEngine.UI;                      // UI 组件（Button、Text、Image、Slider、InputField、GridLayoutGroup 等）
using Random = UnityEngine.Random;         // 给 Random 起别名，避免和 System.Random 冲突

/// <summary>
/// 主菜单管理器（LevelScene 场景的总指挥）。
///
/// 【这个脚本负责什么？】
/// 主菜单（LevelScene）里几乎所有交互都由它统一管理：
///   1. 分类按钮生成与预览（从 AssetBundle 读取图片分类）
///   2. 图片选择、难度选择、金币购买解锁
///   3. 上传图片（从相册选图 → 保存到本地 → 显示列表）
///   4. 局域网分享（分享方/接收方的 UI 交互）
///   5. 每日拼图、看广告回体力、体力/金币显示
///   6. 个人信息（改名字、换头像、等级经验）
///   7. 我的收藏
///   8. 上传/分享的全过程 Debug 日志（显示到 uploadDebugText）
///
/// 【关键设计】
///   - 面板分层：Base / Layer2 / Layer3 / Layer4，由 ShowPanel 统一调度
///   - 防重入：isImagePanelLoading 等标记，防止用户连点导致 UI 重复创建
///   - 动态纹理释放：上传/共享图片运行时创建的 Sprite 用完要 Destroy
///   - 单例模式：让 LANShareManager 等外部模块能访问 Instance 写日志
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    #region 单例（供 LANShareManager 输出日志）

    /// <summary>
    /// 当前主菜单实例（全局唯一）。
    /// 
    /// 【为什么要暴露静态引用？】
    /// LANShareManager 在后台线程收到网络数据后，
    /// 需要往主菜单的 uploadDebugText 写日志。
    /// 通过 MainMenuManager.Instance 就能跨脚本访问到它。
    /// 
    /// 【注意】
    /// 只有主菜单场景（LevelScene）里才有这个实例。
    /// 切到游戏场景（GameScene）后 Instance 会被清空（null）。
    /// </summary>
    public static MainMenuManager Instance { get; private set; }

    #endregion

    #region UI 引用 - 设置面板

    // [Header] 只是让 Inspector 面板里显示分组标题，不影响逻辑。
    // 下面这些 public 字段都需要在 Unity 编辑器里手动拖引用。

    [Header("设置面板")]
    public GameObject settingsPanel;       // 设置面板的根物体（控制显隐）
    public Button settingsButton;          // 打开设置面板的按钮
    public Button settingsCloseButton;     // 关闭设置面板的按钮
    public Button stopBGMButton;           // 停止/播放背景音乐的按钮
    public Slider bgmSlider;               // 背景音乐音量滑条（0~1）
    public Slider sfxSlider;               // 音效音量滑条（0~1）

    [Header("退出游戏")]
    public Button quitGameButton;          // 退出游戏按钮

    [Header("加载音效")]
    public AudioClip loadingSound;         // 切换场景时播放的加载音效

    #endregion

    #region UI 引用 - 局域网分享

    [Header("停止广播")]
    public Button stopBroadcastButton;     // 断开连接 / 停止分享按钮

    [Header("局域网分享 UI")]
    public GameObject deviceListPanel;     // 设备列表面板（接收方搜索到的设备显示在这里）
    public RectTransform deviceListContent;// 设备列表的容器（VerticalLayoutGroup）
    public GameObject deviceButtonPrefab;  // 设备按钮预制体（每个设备一个按钮）
    public float deviceButtonWidth = 620f; // 设备按钮的宽度
    public float deviceButtonHeight = 100f;// 设备按钮的高度
    public Button cancelDiscoverButton;    // 取消搜索设备按钮

    public GameObject remoteImagePanel;    // 远程图片面板（接收方等待下载的界面）
    public RectTransform remoteImageContent;
    public GameObject remoteImageButtonPrefab;
    public Button cancelConnectButton;     // 取消连接按钮
    public Button downloadSelectedButton;  // 【确认下载】按钮
    public Text remoteStatusText;          // 接收方状态文字（显示"等待对方分享"等）

    public GameObject shareSelectPanel;    // 分享选择面板（分享方勾选要传哪些图片）
    public RectTransform shareSelectContent;
    public Button startSharingButton;      // 【分享】按钮（确认要分享选中的图片）
    public Button cancelShareSelectButton; // 取消分享选择按钮

    [Header("局域网分享")]
    public Button shareButton;             // 主界面【分享】按钮
    public Button receiveButton;           // 主界面【接收】按钮
    public Text shareStatusText;           // 分享方状态文字（显示"等待客户端连接"等）

    [Header("共享分类")]
    public GameObject sharedImagePanel;    // 共享图片展示面板（显示下载来的图片）
    public RectTransform sharedImageContent;
    public Button sharedCloseButton;       // 关闭共享面板按钮

    #endregion

    #region UI 引用 - 个人信息

    [Header("修改名字")]
    public Button changeNameButton;        // 打开改名字面板的按钮

    [Header("换头像")]
    public Button changeAvatarButton;      // 打开换头像面板的按钮
    public GameObject avatarSelectPanel;   // 头像选择面板
    public RectTransform avatarScrollContent; // 头像列表容器
    public GameObject avatarButtonPrefab;  // 单个头像按钮预制体
    public Button avatarCloseButton;       // 关闭头像选择面板
    public Image profileAvatarImage;       // 个人信息页显示的头像

    [Header("上传图片")]
    public Button uploadButtonInProfile;   // 个人信息页的"上传图片"按钮
    public GameObject uploadManagePanel;   // 上传管理面板（管理已上传的图片）
    public RectTransform uploadScrollContent; // 上传图片列表容器
    public Button uploadCloseButton;       // 关闭上传管理面板
    public Button addImageButton;          // 【添加图片】按钮（从相册选图）

    [Header("个人信息")]
    public GameObject profilePanel;        // 个人信息面板根物体
    public Text profileNameText;           // 显示玩家名字
    public Text profileLevelText;          // 显示玩家等级和经验
    public Slider experienceSlider;        // 经验进度条
    public Button favoritesButton;         // 打开我的收藏按钮

    [Header("姓名输入面板")]
    public GameObject nameInputPanel;      // 输入名字的面板（首次游戏弹出）
    public InputField nameInputField;      // 名字输入框
    public Button nameConfirmButton;       // 确认名字按钮

    [Header("我的收藏面板")]
    public GameObject favoritesPanel;      // 收藏面板根物体
    public RectTransform favoritesScrollContent;
    public Button favoritesCloseButton;    // 关闭收藏面板

    #endregion

    #region UI 引用 - 分类与图片

    [Header("分类 ScrollView")]
    public GameObject categoryScrollView;  // 分类列表（主界面主体）
    public RectTransform categoryScrollContent; // 分类按钮的容器
    public GameObject categoryButtonPrefab;// 分类按钮预制体

    [Header("分类预览设置")]
    public float previewChangeInterval = 3f; // 预览图切换间隔（秒）
    public float fadeDuration = 0.5f;        // 预览图淡入淡出时长（秒）

    [Header("图片选择面板")]
    public GameObject imageSelectPanel;    // 某个分类的图片列表面板
    public Button closeImagePanelButton;   // 关闭图片面板
    public RectTransform imageScrollContent;
    public GameObject imageButtonPrefab;   // 单个图片按钮预制体

    [Header("难度面板")]
    public GameObject difficultyPanel;     // 难度选择面板
    public Button easyButton;              // 简单难度
    public Button normalButton;            // 普通难度
    public Button hardButton;              // 困难难度
    public Button difficultyCancelButton;  // 取消选择

    [Header("购买面板")]
    public GameObject purchasePanel;       // 购买确认面板
    public Text purchaseText;              // 显示"是否花费 X 金币解锁"
    public Button confirmPurchaseButton;   // 确认购买
    public Button cancelPurchaseButton;    // 取消购买

    #endregion

    #region UI 引用 - 通用

    [Header("每日拼图")]
    public Button dailyPuzzleButton;       // 每日拼图入口按钮

    [Header("加载面板")]
    public GameObject loadingPanel;        // 切场景时的加载面板
    public Text loadingText;               // 加载进度文字
    public Slider loadingSlider;           // 加载进度条

    [Header("上传实时 Debug")]
    [Tooltip("把上传全过程日志显示到主菜单的 Text 上，方便真机排查。")]
    public Text uploadDebugText;           // 显示调试日志的 Text（Unity 里挂在 UI 上）
    [Tooltip("Debug Text 最多保留多少行。")]
    public int uploadDebugMaxLines = 80;   // 日志最大行数（超过就丢弃最老的）

    [Header("通用确认弹窗")]
    public GameObject confirmPanel;        // 通用确认弹窗根物体
    public Text confirmText;               // 弹窗显示的内容
    public Button confirmYesButton;        // 弹窗的"是"按钮
    public Button confirmNoButton;         // 弹窗的"否"按钮

    [Header("广告恢复")]
    public Button adStaminaButton;         // 看广告回体力的按钮

    [Header("体力显示")]
    public Text staminaText;               // 体力数值文字
    public Slider staminaSlider;           // 体力进度条

    [Header("金币显示")]
    public Text coinText;                  // 金币数值文字

    [Header("底部按钮")]
    public Button profileButton;           // 底部：切换到个人信息面板
    public Button categoryButton;          // 底部：切换到分类面板

    #endregion

    #region 私有状态（脚本内部使用，Inspector 不显示）

    private bool isFlashing = false;              // 金币不足闪烁中标记（防止重复触发闪烁）
    private string selectedCategory;              // 当前选中的分类名（如 "Kazimierz"）
    private int selectedImageIndex = -1;          // 当前选中的图片索引
    private string pendingPurchaseCategory;       // 待购买的分类（用户确认购买前暂存）
    private int pendingPurchaseImageIndex;        // 待购买的图片索引

    // ---------- 面板层级管理 ----------
    // 面板分四层，越靠上层越"浮"在屏幕上方。
    // ShowPanel 会根据要显示的面板自动隐藏它上面的层。
    // 例：显示 Layer2 面板时，Layer3/Layer4 会被隐藏。
    private GameObject currentBasePanel;          // 当前基础面板（分类 or 个人信息）
    private GameObject currentLayer2Panel;        // 当前第二层面板
    private GameObject currentLayer3Panel;        // 当前第三层面板（难度选择）
    private GameObject currentLayer4Panel;        // 当前第四层面板（购买）

    // ---------- 预览图协程缓存 ----------
    // 每个分类按钮都有一个"不断切换预览图"的协程。
    // 用字典存起来，方便刷新分类按钮时统一停止（避免协程泄漏）。
    private Dictionary<Button, Coroutine> previewCoroutines = new Dictionary<Button, Coroutine>();

    private Action confirmAction;                 // 通用确认弹窗的"是"回调
    private GameObject panelAfterNameChange;      // 改完名字后要返回的面板
    private Sprite[] avatarSprites;               // 头像 Sprite 缓存（避免每次都从 Resources 加载）

    // ---------- 局域网分享状态 ----------
    private List<string> discoveredDevices = new List<string>(); // 已发现的设备（entry 字符串列表）
    private List<string> remoteImageFiles = new List<string>();  // 从服务端拉取的图片列表
    private HashSet<int> selectedRemoteIndices = new HashSet<int>(); // 选中的远程图片（新流程已不用）
    private List<string> shareSelectedFiles = new List<string>();   // 分享时勾选的本地文件
    private string connectedServerIP = null;     // 已连接的服务器 IP

    // ---------- 防重入标志 ----------
    // 某个面板加载时，如果用户又触发加载，直接忽略第二次请求，
    // 避免出现"重复创建 UI 元素"的 bug。
    private bool isImagePanelLoading = false;     // 图片选择面板加载中
    private bool isSharePanelLoading = false;     // 分享选择面板加载中
    private bool isSharedPanelLoading = false;    // 共享分类面板加载中

    // 上传管理面板当前刷新协程（用于刷新前取消旧协程）
    private Coroutine uploadPanelCoroutine = null;

    // ---------- 动态 Sprite 列表 ----------
    // 上传/共享图片是运行时从文件读出来的，
    // 创建的 Sprite 和 Texture 需要手动 Destroy。
    // 用列表统一记录，切面板时一次性释放。
    private List<Sprite> dynamicSprites = new List<Sprite>();

    // ---------- 调试日志行 ----------
    private readonly List<string> uploadDebugLines = new List<string>();

    #endregion

    #region 上传实时 Debug

    /// <summary>
    /// 追加一行调试日志。
    /// 既输出到 Unity 控制台，也显示到 uploadDebugText。
    /// 
    /// 【为什么是 public？】
    /// LANShareManager 需要在主线程把日志刷到这里。
    /// LANShareManager 内部维护了一个后台队列，
    /// 在主线程 Update 里逐条调用本方法。
    /// 
    /// 【时间戳作用】
    /// 每行开头带 [HH:mm:ss.fff]，方便排查"什么时间发生了什么"。
    /// </summary>
    /// <param name="message">日志内容</param>
    public void UploadDebug(string message)
    {
        // 拼上时间戳前缀
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        // 输出到 Unity 控制台（方便 PC 端调试）
        Debug.Log(line);

        // 如果没绑定 Text 就只进控制台
        if (uploadDebugText == null) return;

        // 加到内存列表
        uploadDebugLines.Add(line);

        // 超过上限就移除最老的行（防止内存爆掉）
        int maxLines = Mathf.Max(10, uploadDebugMaxLines);
        while (uploadDebugLines.Count > maxLines)
            uploadDebugLines.RemoveAt(0);

        // 用换行符拼接所有行，一次性赋给 Text
        uploadDebugText.text = string.Join("\n", uploadDebugLines);
    }

    /// <summary>清空调试日志（切场景或重新开始时用）。</summary>
    private void ClearUploadDebug()
    {
        uploadDebugLines.Clear();
        if (uploadDebugText != null)
            uploadDebugText.text = "";
    }

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake 在物体创建时立即调用（早于 Start）。
    /// 这里只做最简单的事：设置单例。
    /// </summary>
    private void Awake()
    {
        // 设置全局单例，让 LANShareManager 等能访问到
        Instance = this;

        // 测试用：清空所有 PlayerPrefs（正式发布时务必注释掉！）
        // GameDataManager.ResetForEditor();
    }

    /// <summary>
    /// Start 在物体第一帧启用时调用。
    /// 这里做所有的初始化：绑定按钮事件、初始化 UI 显示、初始化数据。
    /// </summary>
    private void Start()
    {
        // 清空上次场景遗留的日志，并打印一条"主菜单启动"
        ClearUploadDebug();
        UploadDebug("========== 主菜单启动 ==========");

        // 订阅"分享停止"事件（当 LANShareManager 停止分享时会回调 HandleSharingStopped）
        LANShareManager.Instance.OnSharingStopped += HandleSharingStopped;

        // 启动协程，等 AssetBundle 加载完成后生成分类按钮
        StartCoroutine(WaitForAssetBundle());

        // ========== 设置面板 ==========
        settingsButton.onClick.AddListener(OpenSettingsPanel);
        settingsCloseButton.onClick.AddListener(() => ShowPanel(profilePanel));
        stopBGMButton.onClick.AddListener(ToggleBGM);
        UpdateBGMButtonText();   // 根据当前 BGM 播放状态更新按钮文字

        // 初始化 BGM 音量滑条
        bgmSlider.minValue = 0f;
        bgmSlider.maxValue = 1f;
        bgmSlider.value = SoundManager.Instance != null ? SoundManager.Instance.BGMVolume : 0.5f;
        bgmSlider.onValueChanged.AddListener((v) =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetBGMVolume(v);
        });

        // 初始化音效音量滑条
        sfxSlider.minValue = 0f;
        sfxSlider.maxValue = 1f;
        sfxSlider.value = SoundManager.Instance != null ? SoundManager.Instance.SFXVolume : 1f;
        sfxSlider.onValueChanged.AddListener((v) =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetSFXVolume(v);
        });

        quitGameButton.onClick.AddListener(QuitGame);

        // ========== 停止广播 ==========
        // 点击：停止分享 + 断开连接 + 回到基础面板
        stopBroadcastButton.onClick.AddListener(() =>
        {
            UploadDebug("========== 点击【停止广播】 ==========");
            LANShareManager.Instance.StopSharing();
            LANShareManager.Instance.DisconnectFromServer();
            shareStatusText.text = "连接已断开";
            ShowPanel(currentBasePanel ?? profilePanel);  // ?? 是"如果前面是 null 就用后面"
        });

        // ========== 局域网分享 ==========
        shareButton.onClick.AddListener(OnShareButtonClicked);
        receiveButton.onClick.AddListener(OnReceiveButtonClicked);

        // 取消发现：停止监听广播，回到基础面板
        cancelDiscoverButton.onClick.AddListener(() =>
        {
            UploadDebug("========== 取消发现 ==========");
            LANShareManager.Instance.StopDiscovery();
            ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        // 取消连接：断开 TCP 连接，回到基础面板
        cancelConnectButton.onClick.AddListener(() =>
        {
            UploadDebug("========== 取消连接 ==========");
            LANShareManager.Instance.DisconnectFromServer();
            ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        downloadSelectedButton.onClick.AddListener(DownloadSelectedImages);
        startSharingButton.onClick.AddListener(StartSharingSelectedFiles);

        // 取消分享选择：回到基础面板
        cancelShareSelectButton.onClick.AddListener(() =>
        {
            UploadDebug("========== 取消选择分享文件 ==========");
            ShowPanel(currentBasePanel ?? profilePanel);
        });

        // ========== 个人信息 ==========
        changeNameButton.onClick.AddListener(OnChangeNameClicked);
        dailyPuzzleButton.onClick.AddListener(OnDailyPuzzleClicked);

        changeAvatarButton.onClick.AddListener(OpenAvatarSelectPanel);
        avatarCloseButton.onClick.AddListener(() => ShowPanel(profilePanel));

        uploadButtonInProfile.onClick.AddListener(OpenUploadManagePanel);
        uploadCloseButton.onClick.AddListener(() => ShowPanel(profilePanel));
        addImageButton.onClick.AddListener(OnUploadButtonClicked);

        favoritesButton.onClick.AddListener(() => ShowPanel(favoritesPanel));
        favoritesCloseButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? profilePanel));

        nameConfirmButton.onClick.AddListener(OnNameConfirmed);

        // ========== 通用 ==========
        confirmYesButton.onClick.AddListener(OnConfirmYes);
        confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));

        // 底部导航按钮：切换基础面板
        profileButton.onClick.AddListener(() => ShowPanel(profilePanel));
        categoryButton.onClick.AddListener(() => ShowPanel(categoryScrollView));

        sharedCloseButton.onClick.AddListener(() => ShowPanel(categoryScrollView));

        // 难度面板的取消按钮：返回到上一层（优先 Layer2，其次基础层）
        difficultyCancelButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null) ShowPanel(currentLayer2Panel);
            else ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        // ========== 体力系统初始化 ==========
        GameDataManager.InitStaminaSystem();     // 首次启动时给满体力
        StartCoroutine(UpdateStaminaUI());        // 每秒刷新体力显示

        // 首次游戏让玩家输入名字，否则直接进分类面板
        if (string.IsNullOrEmpty(GameDataManager.PlayerName))
        {
            panelAfterNameChange = categoryScrollView;
            ShowPanel(nameInputPanel);
        }
        else
        {
            ShowPanel(categoryScrollView);
        }

        // ========== 难度按钮 ==========
        // 2×2 = 简单，8×8 = 普通，10×10 = 困难
        easyButton.onClick.AddListener(() => StartGame(2));
        normalButton.onClick.AddListener(() => StartGame(8));
        hardButton.onClick.AddListener(() => StartGame(10));

        // ========== 广告与购买 ==========
        adStaminaButton.onClick.AddListener(OnAdStaminaClicked);

        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);

        // 取消购买：回到上一层
        cancelPurchaseButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null) ShowPanel(currentLayer2Panel);
            else ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        closeImagePanelButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? categoryScrollView));

        // ========== 初始状态 ==========
        UpdateCoinDisplay();
        loadingPanel.SetActive(false);   // 加载面板默认隐藏

        // 这些面板一开始都要隐藏（等用户操作后再显示）
        avatarSelectPanel.SetActive(false);
        shareSelectPanel.SetActive(false);
        deviceListPanel.SetActive(false);
        remoteImagePanel.SetActive(false);
        sharedImagePanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    /// <summary>
    /// OnEnable 在物体每次被激活时调用。
    /// 主要用于"从游戏场景返回主菜单"时刷新数据。
    /// </summary>
    private void OnEnable()
    {
        // 从游戏返回时，金币和玩家数据可能已经变了，需要刷新 UI
        UpdateCoinDisplay();
        UpdateProfileUI();
    }

    /// <summary>
    /// OnDestroy 在物体被销毁时调用。
    /// 清理单例、取消事件订阅、释放动态纹理。
    /// </summary>
    private void OnDestroy()
    {
        // 清空单例（下次进入主菜单会重新赋值）
        if (Instance == this) Instance = null;

        // 取消事件订阅（防止内存泄漏）
        if (LANShareManager.Instance != null)
            LANShareManager.Instance.OnSharingStopped -= HandleSharingStopped;

        // 释放动态创建的 Sprite 和 Texture
        ReleaseDynamicSprites();
    }

    #endregion

    #region AssetBundle 等待

    /// <summary>
    /// 等待 AssetBundleManager 加载完毕，再生成分类按钮。
    /// 
    /// 【为什么要等？】
    /// 分类按钮需要显示预览图，预览图来自 AB 包。
    /// AB 加载是异步的（可能几秒），所以这里用协程轮询等待。
    /// 
    /// 【执行流程】
    /// 1. while 循环每帧检查一次 IsLoaded
    /// 2. 加载完成后，生成分类按钮 + 刷新金币 + 刷新个人信息
    /// </summary>
    private IEnumerator WaitForAssetBundle()
    {
        while (AssetBundleManager.Instance == null || !AssetBundleManager.Instance.IsLoaded)
            yield return null;   // 每帧检查一次，不阻塞主线程

        GenerateCategoryButtons();
        UpdateCoinDisplay();
        UpdateProfileUI();
    }

    #endregion

    #region 面板管理

    /// <summary>
    /// 显示指定面板，并自动隐藏其他不该显示的面板。
    /// 
    /// 【面板分层】
    /// - 基础层（Base）：
    ///     categoryScrollView（分类）、profilePanel（个人信息）
    /// - 第 2 层（Layer2）：
    ///     imageSelectPanel、favoritesPanel、uploadManagePanel、
    ///     avatarSelectPanel、shareSelectPanel、deviceListPanel、
    ///     remoteImagePanel、sharedImagePanel、settingsPanel
    /// - 第 3 层（Layer3）：difficultyPanel（难度选择）
    /// - 第 4 层（Layer4）：purchasePanel（购买）
    /// - 特殊层：nameInputPanel（独占，最高层级）
    /// 
    /// 【核心思路】
    /// 先判断要显示的面板属于哪一层，
    /// 然后隐藏它上面所有层 + 同层的其他面板，
    /// 最后显示目标面板并置顶。
    /// </summary>
    private void ShowPanel(GameObject panelToShow)
    {
        // 参数为 null：只隐藏浮层，不清空基础层
        if (panelToShow == null)
        {
            HideOverlayPanels();
            confirmPanel.SetActive(false);
            return;
        }

        // 切换面板时，先关掉可能还开着的确认弹窗
        confirmPanel.SetActive(false);

        // ---------- 情况 1：基础面板 ----------
        if (panelToShow == categoryScrollView || panelToShow == profilePanel)
        {
            HideOverlayPanels();   // 隐藏所有浮层
            categoryScrollView.SetActive(panelToShow == categoryScrollView);
            profilePanel.SetActive(panelToShow == profilePanel);
            currentBasePanel = panelToShow;
            panelToShow.transform.SetAsLastSibling();   // 放到 UI 最上层（防止被遮挡）
        }
        // ---------- 情况 2：第二层面板 ----------
        else if (panelToShow == imageSelectPanel || panelToShow == favoritesPanel ||
                 panelToShow == uploadManagePanel || panelToShow == avatarSelectPanel ||
                 panelToShow == shareSelectPanel || panelToShow == deviceListPanel ||
                 panelToShow == remoteImagePanel || panelToShow == sharedImagePanel ||
                 panelToShow == settingsPanel)
        {
            HidePanelsAboveLayer2();   // 先隐藏第三、四层

            // 隐藏所有基础层和第二层面板
            categoryScrollView.SetActive(false);
            profilePanel.SetActive(false);

            imageSelectPanel.SetActive(false);
            favoritesPanel.SetActive(false);
            uploadManagePanel.SetActive(false);
            avatarSelectPanel.SetActive(false);
            shareSelectPanel.SetActive(false);
            deviceListPanel.SetActive(false);
            remoteImagePanel.SetActive(false);
            sharedImagePanel.SetActive(false);
            settingsPanel.SetActive(false);

            // 显示目标面板（只显示一个）
            if (panelToShow == imageSelectPanel) imageSelectPanel.SetActive(true);
            else if (panelToShow == favoritesPanel) favoritesPanel.SetActive(true);
            else if (panelToShow == uploadManagePanel) uploadManagePanel.SetActive(true);
            else if (panelToShow == avatarSelectPanel) avatarSelectPanel.SetActive(true);
            else if (panelToShow == shareSelectPanel) shareSelectPanel.SetActive(true);
            else if (panelToShow == deviceListPanel) deviceListPanel.SetActive(true);
            else if (panelToShow == remoteImagePanel) remoteImagePanel.SetActive(true);
            else if (panelToShow == sharedImagePanel) sharedImagePanel.SetActive(true);
            else if (panelToShow == settingsPanel) settingsPanel.SetActive(true);

            currentLayer2Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        // ---------- 情况 3：第三层（难度选择） ----------
        else if (panelToShow == difficultyPanel)
        {
            HidePanelsAboveLayer3();
            difficultyPanel.SetActive(true);
            currentLayer3Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        // ---------- 情况 4：第四层（购买） ----------
        else if (panelToShow == purchasePanel)
        {
            HidePanelsAboveLayer4();
            purchasePanel.SetActive(true);
            currentLayer4Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        // ---------- 情况 5：特殊（姓名输入，独占屏幕） ----------
        else if (panelToShow == nameInputPanel)
        {
            HideAllPanels();   // 全部隐藏，让姓名面板独占
            nameInputPanel.SetActive(true);
            nameInputPanel.transform.SetAsLastSibling();

            // 强制提升 Canvas 层级，确保在所有 UI 之上
            // （姓名面板需要盖住一切，包括可能存在的悬浮按钮）
            Canvas canvas = nameInputPanel.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = 999;
            }
        }

        // ---------- 显示后自动触发的刷新 ----------
        // 某些面板需要在显示时刷新内容（比如从存档读取最新数据）
        if (panelToShow == profilePanel) UpdateProfileUI();
        if (panelToShow == favoritesPanel) PopulateFavoritesPanel();
        if (panelToShow == uploadManagePanel) PopulateUploadManagePanel();
    }

    /// <summary>
    /// 隐藏所有"浮层"面板（Layer2~4 + 姓名输入），
    /// 保留基础层（分类 or 个人信息）。
    /// </summary>
    private void HideOverlayPanels()
    {
        settingsPanel.SetActive(false);
        avatarSelectPanel.SetActive(false);
        imageSelectPanel.SetActive(false);
        favoritesPanel.SetActive(false);
        uploadManagePanel.SetActive(false);
        shareSelectPanel.SetActive(false);
        deviceListPanel.SetActive(false);
        remoteImagePanel.SetActive(false);
        sharedImagePanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);

        // 清空层级记录
        currentLayer2Panel = null;
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    /// <summary>隐藏第二层以上的所有面板（Layer2、3、4 及姓名输入）。</summary>
    private void HidePanelsAboveLayer2()
    {
        settingsPanel.SetActive(false);
        avatarSelectPanel.SetActive(false);
        shareSelectPanel.SetActive(false);
        deviceListPanel.SetActive(false);
        remoteImagePanel.SetActive(false);
        sharedImagePanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);

        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    /// <summary>隐藏第三层以上的所有面板（Layer3、4 及姓名输入）。</summary>
    private void HidePanelsAboveLayer3()
    {
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentLayer4Panel = null;
    }

    /// <summary>隐藏第四层以上的所有面板（Layer4 及姓名输入）。</summary>
    private void HidePanelsAboveLayer4()
    {
        nameInputPanel.SetActive(false);
    }

    /// <summary>隐藏所有面板（包括基础层），用于"全部归零"的场景。</summary>
    private void HideAllPanels()
    {
        settingsPanel.SetActive(false);
        avatarSelectPanel.SetActive(false);
        categoryScrollView.SetActive(false);
        profilePanel.SetActive(false);
        imageSelectPanel.SetActive(false);
        favoritesPanel.SetActive(false);
        uploadManagePanel.SetActive(false);
        shareSelectPanel.SetActive(false);
        deviceListPanel.SetActive(false);
        remoteImagePanel.SetActive(false);
        sharedImagePanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);

        // 清空所有层级记录
        currentBasePanel = null;
        currentLayer2Panel = null;
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    #endregion

    #region 设置面板

    /// <summary>
    /// 打开设置面板（先同步当前音量到滑条）。
    /// </summary>
    private void OpenSettingsPanel()
    {
        // 打开设置前，把 SoundManager 里的当前音量同步到滑条上
        // （否则滑条显示的是上次的值，和实际不符）
        if (SoundManager.Instance != null)
        {
            bgmSlider.value = SoundManager.Instance.BGMVolume;
            sfxSlider.value = SoundManager.Instance.SFXVolume;
        }
        ShowPanel(settingsPanel);
    }

    /// <summary>
    /// 退出游戏（编辑器里用特殊方式，真机上用 Application.Quit）。
    /// </summary>
    private void QuitGame()
    {
#if UNITY_EDITOR
        // Unity 编辑器里 Application.Quit 无效，需要停掉播放模式
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // 真机上正常退出
        Application.Quit();
#endif
    }

    /// <summary>
    /// 切换背景音乐（播放 ↔ 停止）。
    /// </summary>
    private void ToggleBGM()
    {
        if (SoundManager.Instance == null) return;

        if (SoundManager.Instance.IsBGMPlaying)
            SoundManager.Instance.StopBGM();
        else
            SoundManager.Instance.PlayBGM();

        // 切换后更新按钮文字
        UpdateBGMButtonText();
    }

    /// <summary>
    /// 根据当前 BGM 播放状态，更新按钮上的文字。
    /// 正在播放 → "停止背景音乐"，否则 → "播放背景音乐"。
    /// </summary>
    private void UpdateBGMButtonText()
    {
        if (stopBGMButton == null) return;

        // 获取按钮上的 Text 子物体
        Text label = stopBGMButton.GetComponentInChildren<Text>();
        if (label == null) return;

        bool isPlaying = SoundManager.Instance != null && SoundManager.Instance.IsBGMPlaying;
        label.text = isPlaying ? "停止背景音乐" : "播放背景音乐";
    }

    #endregion

    #region 分类按钮生成

    /// <summary>
    /// 生成分类按钮（主界面的核心 UI）。
    /// 
    /// 创建 6 个普通分类按钮 + 1 个"上传"按钮 + 1 个"共享"按钮，共 8 个。
    /// 
    /// 【为什么要先清理旧按钮？】
    /// 因为每次调用本方法都会重新生成，
    /// 如果不清理会产生"旧按钮 + 新按钮"重叠的 bug。
    /// 同时也要停止旧的预览协程，避免泄漏。
    /// </summary>
    private void GenerateCategoryButtons()
    {
        // 先停掉所有正在运行的预览图协程
        foreach (var kvp in previewCoroutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        previewCoroutines.Clear();

        // 销毁所有旧的分类按钮
        // 注意：用 for 反向遍历更安全（Destroy 会让 foreach 出问题）
        foreach (Transform child in categoryScrollContent)
            Destroy(child.gameObject);

        // 设置网格布局：一行两列，每个格子 350×350
        GridLayoutGroup grid = categoryScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = categoryScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(350, 350);
        grid.spacing = new Vector2(50, 50);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        // ---------- 普通分类按钮 ----------
        for (int i = 0; i < GameDataManager.Categories.Length; i++)
        {
            string category = GameDataManager.Categories[i];

            // 复制预制体
            GameObject btnObj = Instantiate(categoryButtonPrefab, categoryScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Text label = btnObj.GetComponentInChildren<Text>();
            Image previewImage = btnObj.transform.Find("PreviewImage")?.GetComponent<Image>();

            if (label != null) label.text = category;

            // 闭包捕获：必须用局部变量
            // （否则所有按钮的 index 都是循环结束时的值，会是同一个数）
            int index = i;
            btn.onClick.AddListener(() => OnCategoryClicked(index));

            // 启动预览图协程（不断切换预览图）
            if (previewImage != null)
            {
                Coroutine coroutine = StartCoroutine(UpdateCategoryPreview(previewImage, category));
                previewCoroutines[btn] = coroutine;   // 记录协程，便于后续停止
            }
        }

        // ---------- "上传"分类按钮 ----------
        // 它是特殊分类，点击后显示用户上传的图片
        GameObject uploadBtnObj = Instantiate(categoryButtonPrefab, categoryScrollContent);
        Button uploadBtn = uploadBtnObj.GetComponent<Button>();
        Text uploadLabel = uploadBtnObj.GetComponentInChildren<Text>();
        Image uploadPreview = uploadBtnObj.transform.Find("PreviewImage")?.GetComponent<Image>();
        if (uploadLabel != null) uploadLabel.text = "上传";
        if (uploadPreview != null) uploadPreview.sprite = null;   // 上传按钮无预览图
        uploadBtn.onClick.AddListener(() => OnCategoryClicked(GameDataManager.Categories.Length));

        // ---------- "共享"分类按钮 ----------
        // 显示从其他设备下载来的图片
        GameObject sharedBtnObj = Instantiate(categoryButtonPrefab, categoryScrollContent);
        Button sharedBtn = sharedBtnObj.GetComponent<Button>();
        Text sharedLabel = sharedBtnObj.GetComponentInChildren<Text>();
        Image sharedPreview = sharedBtnObj.transform.Find("PreviewImage")?.GetComponent<Image>();
        if (sharedLabel != null) sharedLabel.text = "共享";
        if (sharedPreview != null) sharedPreview.sprite = null;
        sharedBtn.onClick.AddListener(OnSharedCategoryClicked);
    }

    #endregion

    #region 体力显示

    /// <summary>
    /// 每秒刷新一次体力显示。
    /// 用无限循环的协程实现（只要 MainMenuManager 存在就一直跑）。
    /// 
    /// 【为什么用协程而不是 Update？】
    /// 体力是每 60 秒才恢复 1 点，没必要每帧刷新。
    /// 每秒刷新已经足够。
    /// </summary>
    private IEnumerator UpdateStaminaUI()
    {
        while (true)
        {
            UpdateStaminaDisplay();
            yield return new WaitForSeconds(1f);
        }
    }

    /// <summary>刷新体力文字和进度条。</summary>
    private void UpdateStaminaDisplay()
    {
        if (staminaText != null)
            staminaText.text = $"体力：{GameDataManager.Stamina}/{GameDataManager.MaxStamina}";

        if (staminaSlider != null)
        {
            staminaSlider.maxValue = GameDataManager.MaxStamina;
            staminaSlider.value = GameDataManager.Stamina;
            staminaSlider.interactable = false;   // 只显示，不允许用户拖动
        }
    }

    #endregion

    #region 分类预览

    /// <summary>
    /// 分类按钮的预览图循环。
    /// 每隔 previewChangeInterval 秒切换一张随机图片，带淡入淡出效果。
    /// 
    /// 【性能优化】
    /// 只在分类面板可见时才运行，隐藏时跳过（避免无意义的 Canvas Rebuild）。
    /// </summary>
    private IEnumerator UpdateCategoryPreview(Image previewImage, string category)
    {
        if (previewImage == null) yield break;

        // 拿到该分类下所有 Sprite
        Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites(category);
        if (sprites.Length == 0) yield break;

        // 无限循环（只要按钮还存在就一直运行）
        while (previewImage != null)
        {
            // ★ 面板不可见时跳过本轮
            // 这一步很关键：如果不跳过，6 个按钮同时每帧改 color 会触发
            // 大量 Canvas Rebuild，CPU 会飙到 30% 左右。
            if (categoryScrollView == null || !categoryScrollView.activeInHierarchy)
            {
                yield return null;
                continue;
            }

            // 随机选一张，然后淡入淡出
            Sprite newSprite = sprites[Random.Range(0, sprites.Length)];
            yield return StartCoroutine(FadeToSprite(previewImage, newSprite));

            if (previewImage == null) yield break;

            // 停留几秒再切换下一张
            yield return new WaitForSeconds(previewChangeInterval);
        }
    }

    /// <summary>
    /// 淡出旧图 → 换图 → 淡入新图。
    /// 
    /// 【性能优化说明】
    /// 之前每帧改 image.color，60 FPS 时每秒改 60 次，每次都触发 Canvas Rebuild。
    /// 改成每 0.05 秒改一次（20 次/秒），视觉上几乎看不出差别，CPU 降约 3 倍。
    /// </summary>
    private IEnumerator FadeToSprite(Image image, Sprite newSprite)
    {
        if (image == null) yield break;

        const float step = 0.05f;   // 每 0.05 秒更新一次颜色

        float elapsed = 0f;
        Color startColor = image.color;
        Color transparentColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        // ---------- 淡出 ----------
        while (elapsed < fadeDuration)
        {
            if (image == null) yield break;

            elapsed += step;
            float t = Mathf.Clamp01(elapsed / fadeDuration);   // 归一化到 0~1
            image.color = Color.Lerp(startColor, transparentColor, t);

            yield return new WaitForSeconds(step);
        }

        if (image == null) yield break;

        // ---------- 换图（在完全透明时进行，用户看不到切换瞬间） ----------
        image.sprite = newSprite;

        // ---------- 淡入 ----------
        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            if (image == null) yield break;

            elapsed += step;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            image.color = Color.Lerp(transparentColor, startColor, t);

            yield return new WaitForSeconds(step);
        }

        // 最后确保恢复到原始颜色
        if (image != null) image.color = startColor;
    }

    #endregion

    #region 分类点击与图片选择

    /// <summary>
    /// 点击分类按钮。
    /// index == Categories.Length 表示点击了"上传"按钮（特殊处理）。
    /// </summary>
    private void OnCategoryClicked(int index)
    {
        // 点击"上传"分类
        if (index == GameDataManager.Categories.Length)
        {
            selectedCategory = GameDataManager.UploadCategory;
            ShowPanel(imageSelectPanel);
            StartCoroutine(OpenImageSelectPanelAsync(GameDataManager.UploadCategory));
            return;
        }

        // 点击普通分类
        string category = GameDataManager.Categories[index];
        selectedCategory = category;
        ShowPanel(imageSelectPanel);
        StartCoroutine(OpenImageSelectPanelAsync(category));
    }

    /// <summary>
    /// 异步填充图片选择面板。
    /// 
    /// 【为什么要异步？】
    /// 上传分类的图片需要从文件系统读取 + 解码，可能几十到几百毫秒。
    /// 用协程让出主线程，避免 UI 卡顿。
    /// 
    /// 【防重入】
    /// 如果用户连点分类按钮，第一次还没加载完就忽略后续请求，
    /// 避免 UI 元素重复创建。
    /// </summary>
    private IEnumerator OpenImageSelectPanelAsync(string category)
    {
        // 防重入：上一次还没加载完就忽略
        if (isImagePanelLoading)
        {
            Debug.Log("图片面板正在加载中，忽略本次请求");
            yield break;
        }
        isImagePanelLoading = true;

        // 释放上一次的动态 Sprite（上传/共享来源）
        ReleaseDynamicSprites();

        // 清理旧按钮
        foreach (Transform child in imageScrollContent)
            Destroy(child.gameObject);

        // 设置网格布局：一行三列，每个 300×300
        GridLayoutGroup grid = imageScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = imageScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(300, 300);
        grid.spacing = new Vector2(40, 40);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        // ---------- 上传分类：从文件系统异步加载 ----------
        if (category == GameDataManager.UploadCategory)
        {
            List<string> files = GameDataManager.GetUploadedImages();
            for (int i = 0; i < files.Count; i++)
            {
                string path = GameDataManager.GetUploadedImagePath(files[i]);
                if (!File.Exists(path)) continue;   // 文件不存在就跳过

                Sprite sprite = null;
                // yield return 等异步加载完成（后台线程读文件+解码，主线程创建 Sprite）
                yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => sprite = s);
                if (sprite == null) continue;

                dynamicSprites.Add(sprite);   // 记录，便于后续释放
                CreateImageButton(category, i, sprite, files[i]);

                yield return null;   // 每张让出一帧，保持 UI 流畅
            }
        }
        // ---------- 普通分类：从 AssetBundle 同步加载 ----------
        else
        {
            Sprite[] loadedSprites = AssetBundleManager.Instance.GetCategorySprites(category);
            // 按名字排序，保证顺序稳定
            Array.Sort(loadedSprites, (a, b) => string.Compare(a.name, b.name));

            for (int i = 0; i < loadedSprites.Length; i++)
            {
                CreateImageButton(category, i, loadedSprites[i], loadedSprites[i].name);
            }
        }

        // 强制重建布局（确保 UI 立即显示正确）
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(imageScrollContent);

        isImagePanelLoading = false;
    }

    /// <summary>
    /// 在图片选择面板中创建一个图片按钮。
    /// </summary>
    private void CreateImageButton(string category, int imageIndex, Sprite sprite, string name)
    {
        GameObject imgBtnObj = Instantiate(imageButtonPrefab, imageScrollContent);
        Button imgBtn = imgBtnObj.GetComponent<Button>();
        Image img = imgBtnObj.transform.Find("Image")?.GetComponent<Image>();
        Text label = imgBtnObj.GetComponentInChildren<Text>();

        if (img != null) img.sprite = sprite;

        if (category == GameDataManager.UploadCategory)
        {
            // 上传分类：不显示价格标签
            if (label != null) label.text = "";
        }
        else
        {
            // 普通分类：判断是否解锁
            bool unlocked = GameDataManager.IsImageUnlocked(category, imageIndex);
            if (img != null)
                // 未解锁的图片显示半透明灰
                img.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.7f);
            if (label != null)
                // 未解锁的显示价格
                label.text = unlocked ? "" : $"{GameDataManager.GetImagePrice(category, imageIndex)}金币";
        }

        // 闭包捕获：imageIndex 会被 lambda 捕获，所以要用局部变量
        int idx = imageIndex;
        imgBtn.onClick.AddListener(() => OnImageClicked(idx));
    }

    /// <summary>
    /// 点击某张图片。
    /// - 已解锁 → 进难度选择面板
    /// - 未解锁 → 进购买面板
    /// </summary>
    private void OnImageClicked(int imageIndex)
    {
        // 上传分类：直接进难度选择（上传的图片不需要解锁）
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            selectedImageIndex = imageIndex;
            ShowPanel(difficultyPanel);
            return;
        }

        if (imageIndex == -1)
        {
            // -1 表示"随机图片"
            selectedImageIndex = -1;
            ShowPanel(difficultyPanel);
        }
        else
        {
            string category = selectedCategory;
            if (GameDataManager.IsImageUnlocked(category, imageIndex))
            {
                // 已解锁 → 直接进难度选择
                selectedImageIndex = imageIndex;
                ShowPanel(difficultyPanel);
            }
            else
            {
                // 未解锁 → 弹购买确认
                pendingPurchaseCategory = category;
                pendingPurchaseImageIndex = imageIndex;
                int price = GameDataManager.GetImagePrice(category, imageIndex);
                purchaseText.text = $"是否花费 {price} 金币解锁这张图片？";
                ShowPanel(purchasePanel);
            }
        }
    }

    #endregion

    #region 购买逻辑

    /// <summary>
    /// 确认购买。
    /// 根据 pendingPurchaseImageIndex 是否为 -1 区分是"买分类"还是"买图片"。
    /// 本项目分类都免费，所以实际只会走"买图片"分支。
    /// </summary>
    private void ConfirmPurchase()
    {
        // ---------- 分类购买（保留逻辑，当前用不到） ----------
        if (pendingPurchaseImageIndex == -1)
        {
            int price = GameDataManager.CategoryPrices[Array.IndexOf(GameDataManager.Categories, pendingPurchaseCategory)];
            if (GameDataManager.SpendCoins(price))
            {
                GameDataManager.UnlockCategory(pendingPurchaseCategory);
                UpdateCoinDisplay();
                GenerateCategoryButtons();
                ShowPanel(categoryScrollView);
                Debug.Log($"解锁分类 {pendingPurchaseCategory} 成功！");
            }
            else
            {
                purchaseText.text = "金币不足！";
                StartCoroutine(FlashCoinTextRed());
            }
        }
        // ---------- 图片购买 ----------
        else
        {
            int price = GameDataManager.GetImagePrice(pendingPurchaseCategory, pendingPurchaseImageIndex);
            if (GameDataManager.SpendCoins(price))
            {
                // 扣款成功 → 解锁图片
                GameDataManager.UnlockImage(pendingPurchaseCategory, pendingPurchaseImageIndex);
                UpdateCoinDisplay();
                ShowPanel(imageSelectPanel);
                // 刷新图片列表，让刚解锁的图片显示正常
                StartCoroutine(OpenImageSelectPanelAsync(pendingPurchaseCategory));
                Debug.Log($"解锁图片 {pendingPurchaseCategory}_{pendingPurchaseImageIndex} 成功！");
            }
            else
            {
                // 金币不足 → 提示 + 闪烁
                purchaseText.text = "金币不足！";
                StartCoroutine(FlashCoinTextRed());
            }
        }
    }

    /// <summary>
    /// 金币不足时让金币文字闪红三次。
    /// </summary>
    private IEnumerator FlashCoinTextRed()
    {
        // 防重复：正在闪的时候不要再来一次
        if (coinText == null || isFlashing) yield break;

        isFlashing = true;
        Color originalColor = coinText.color;

        // 闪红 → 恢复 → 闪红 → 恢复 → 闪红 → 恢复
        coinText.color = Color.red;
        yield return new WaitForSeconds(0.2f);
        coinText.color = originalColor;
        yield return new WaitForSeconds(0.2f);
        coinText.color = Color.red;
        yield return new WaitForSeconds(0.2f);
        coinText.color = originalColor;

        isFlashing = false;
    }

    #endregion

    #region 上传图片管理

    /// <summary>打开上传管理面板。</summary>
    private void OpenUploadManagePanel()
    {
        UploadDebug("打开上传管理面板");
        ShowPanel(uploadManagePanel);
    }

    /// <summary>
    /// 请求刷新上传管理面板。
    /// 如果旧刷新还在进行，先取消它，再启动新的（避免两个协程同时创建按钮）。
    /// </summary>
    private void PopulateUploadManagePanel()
    {
        UploadDebug("请求刷新上传管理面板");

        // 取消旧协程
        if (uploadPanelCoroutine != null)
        {
            UploadDebug("发现旧的上传 UI 刷新协程，停止旧协程");
            StopCoroutine(uploadPanelCoroutine);
            uploadPanelCoroutine = null;
        }

        // 检查引用是否都绑定了（提前检查，避免出现 null 异常）
        if (uploadManagePanel == null) { UploadDebug("ERROR: uploadManagePanel 未绑定"); return; }
        if (uploadScrollContent == null) { UploadDebug("ERROR: uploadScrollContent 未绑定"); return; }
        if (imageButtonPrefab == null) { UploadDebug("ERROR: imageButtonPrefab 未绑定"); return; }

        // 启动新的刷新协程
        uploadPanelCoroutine = StartCoroutine(PopulateUploadManagePanelAsync());
    }

    /// <summary>
    /// 把 PNG 字节保存到 Uploads 目录。
    /// 
    /// 【执行步骤】
    /// 1. 验证 PNG 有效性（用 LoadImage 试解码一次）
    /// 2. 写入文件到 Uploads 目录
    /// 3. 把文件名写入 PlayerPrefs 的 UploadImages 列表
    /// 4. 刷新分类按钮（如果分类面板可见）
    /// 5. 刷新上传管理 UI
    /// </summary>
    private IEnumerator SaveUploadedPng(byte[] pngBytes)
    {
        UploadDebug("进入 SaveUploadedPng()");

        // ---------- 参数检查 ----------
        if (pngBytes == null) { UploadDebug("ERROR: pngBytes == null"); ShowConfirm("上传失败:\nPNG 编码失败", null); yield break; }
        UploadDebug($"PNG bytes = {pngBytes.Length}");
        if (pngBytes.Length < 100) { UploadDebug("ERROR: PNG 字节长度 < 100"); ShowConfirm("上传失败:\nPNG 编码失败", null); yield break; }

        // ---------- 1. 验证 PNG 有效性 ----------
        // 为什么？有时上游生成的字节数组可能损坏，写入前先验证一遍更保险。
        Texture2D testTex = null;
        try
        {
            testTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool valid = testTex.LoadImage(pngBytes);
            if (!valid)
            {
                UploadDebug("ERROR: Texture2D.LoadImage(PNG) 返回 false");
                Destroy(testTex);
                ShowConfirm("上传失败:\nPNG 无法解析", null);
                yield break;
            }
            UploadDebug($"PNG 验证成功: {testTex.width}x{testTex.height}");
        }
        catch (Exception e)
        {
            UploadDebug($"ERROR: PNG 验证异常: {e}");
            if (testTex != null) Destroy(testTex);
            ShowConfirm("上传失败:\nPNG 验证异常", null);
            yield break;
        }
        if (testTex != null) Destroy(testTex);

        // ---------- 2. 写入 Uploads 目录 ----------
        string uploadDir = Path.Combine(Application.persistentDataPath, "Uploads");
        UploadDebug($"Uploads 目录: {uploadDir}");

        try
        {
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
                UploadDebug("创建 Uploads 目录成功");
            }
        }
        catch (Exception e)
        {
            UploadDebug($"ERROR: 创建 Uploads 目录失败: {e}");
            ShowConfirm("上传失败:\n无法创建 Uploads 目录", null);
            yield break;
        }

        // 用时间戳作为文件名，精确到毫秒，保证不重名
        string fileName = "upload_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
        string destPath = Path.Combine(uploadDir, fileName);
        UploadDebug($"准备保存: {destPath}");

        try
        {
            File.WriteAllBytes(destPath, pngBytes);
            bool exists = File.Exists(destPath);
            long length = exists ? new FileInfo(destPath).Length : 0;
            UploadDebug($"文件保存完成: exists={exists}, length={length}");

            if (!exists || length <= 0)
            {
                UploadDebug("ERROR: 保存后文件不存在或长度为 0");
                ShowConfirm("上传失败:\n文件保存失败", null);
                yield break;
            }
        }
        catch (Exception e)
        {
            UploadDebug($"ERROR: File.WriteAllBytes 失败: {e}");
            ShowConfirm($"上传失败:\n写入文件失败\n{e.Message}", null);
            yield break;
        }

        // ---------- 3. 写入数据列表（PlayerPrefs） ----------
        try
        {
            GameDataManager.AddUploadedImage(fileName);
            List<string> savedFiles = GameDataManager.GetUploadedImages();
            UploadDebug($"PlayerPrefs 写入成功，当前上传列表数量: {savedFiles.Count}");

            // 验证写入是否生效（防止 PlayerPrefs 意外失败）
            bool recorded = savedFiles.Contains(fileName);
            UploadDebug($"检查新文件是否在列表中: {recorded} ({fileName})");

            if (!recorded)
            {
                UploadDebug("ERROR: 文件已保存，但未成功写入上传列表");
                ShowConfirm("上传失败:\n上传记录保存失败", null);
                yield break;
            }
        }
        catch (Exception e)
        {
            UploadDebug($"ERROR: GameDataManager.AddUploadedImage 异常: {e}");
            ShowConfirm("上传失败:\n上传记录保存异常", null);
            yield break;
        }

        // ---------- 4. 刷新分类按钮（如果当前可见） ----------
        if (categoryScrollView != null && categoryScrollView.activeSelf)
        {
            UploadDebug("当前分类面板可见，刷新分类按钮");
            GenerateCategoryButtons();
        }
        else
        {
            UploadDebug("分类面板当前不可见，跳过分类按钮刷新");
        }

        // ---------- 5. 刷新上传管理 UI ----------
        UploadDebug("开始刷新上传管理 UI");
        PopulateUploadManagePanel();

        yield return null;

        UploadDebug("SaveUploadedPng() 完成");
    }

    /// <summary>
    /// 异步刷新上传管理面板。
    /// 遍历已上传文件列表，为每个文件创建一个缩略图按钮。
    /// 
    /// 【按钮功能】
    /// 点击删除对应的上传图片（同时删文件和 PlayerPrefs 记录）。
    /// </summary>
    private IEnumerator PopulateUploadManagePanelAsync()
    {
        UploadDebug("========== 开始刷新上传 UI ==========");

        // 引用检查
        if (uploadScrollContent == null) { UploadDebug("ERROR: uploadScrollContent == null"); uploadPanelCoroutine = null; yield break; }
        if (imageButtonPrefab == null) { UploadDebug("ERROR: imageButtonPrefab == null"); uploadPanelCoroutine = null; yield break; }

        // 释放旧 Sprite（避免显存泄漏）
        UploadDebug($"清理 dynamicSprites，当前数量={dynamicSprites.Count}");
        ReleaseDynamicSprites();

        // 清理旧按钮
        int oldChildCount = uploadScrollContent.childCount;
        UploadDebug($"清理旧上传按钮，childCount={oldChildCount}");
        foreach (Transform child in uploadScrollContent)
            Destroy(child.gameObject);

        yield return null;   // 等一帧，让 Destroy 生效

        // 设置网格布局
        GridLayoutGroup grid = uploadScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = uploadScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 200);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 自适应高度
        ContentSizeFitter fitter = uploadScrollContent.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = uploadScrollContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // 读取已上传文件列表
        List<string> files = GameDataManager.GetUploadedImages();
        UploadDebug($"从 GameDataManager 读取上传列表: count={files.Count}");

        int createdCount = 0;

        foreach (string file in files)
        {
            if (string.IsNullOrEmpty(file)) { UploadDebug("WARNING: 空文件名，跳过"); continue; }

            string path = GameDataManager.GetUploadedImagePath(file);
            bool exists = File.Exists(path);
            UploadDebug($"检查[{createdCount}] file={file}, exists={exists}");

            if (!exists) { UploadDebug($"WARNING: 文件不存在，跳过: {path}"); continue; }

            // 打印文件大小用于诊断
            long length = 0;
            try { length = new FileInfo(path).Length; } catch { }
            UploadDebug($"文件大小: {length} bytes");

            // 异步加载 Sprite
            Sprite sprite = null;
            yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => sprite = s);

            if (sprite == null) { UploadDebug($"ERROR: ImageLoader 创建 Sprite 失败: {file}"); continue; }

            UploadDebug($"Sprite 创建成功: {sprite.name}, {sprite.texture.width}x{sprite.texture.height}");
            dynamicSprites.Add(sprite);

            // 创建按钮（用 try-catch 防止预制体异常）
            GameObject btnObj = null;
            try { btnObj = Instantiate(imageButtonPrefab, uploadScrollContent); }
            catch (Exception e) { UploadDebug($"ERROR: Instantiate 异常: {e}"); continue; }

            if (btnObj == null) { UploadDebug("ERROR: Instantiate 返回 null"); continue; }

            Button btn = btnObj.GetComponent<Button>();
            if (btn == null) UploadDebug($"WARNING: 按钮 Prefab 上没有 Button: {btnObj.name}");

            // 找 Image 组件（优先找名为 Image 的子节点）
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img == null) img = btnObj.GetComponentInChildren<Image>(true);

            UploadDebug($"按钮创建成功: {btnObj.name}, Image={img != null}");

            if (img != null)
            {
                img.enabled = true;
                img.sprite = sprite;
                img.color = Color.white;
            }

            // 绑定删除事件
            if (btn != null)
            {
                string fileNameForButton = file;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    UploadDebug($"点击删除上传图片: {fileNameForButton}");
                    GameDataManager.RemoveUploadedImage(fileNameForButton);
                    PopulateUploadManagePanel();   // 刷新
                });
            }

            // 显示"删除"文字
            Text label = btnObj.GetComponentInChildren<Text>(true);
            if (label != null) label.text = "删除";

            createdCount++;
            UploadDebug($"上传图片 UI 创建完成: {createdCount}/{files.Count}");

            yield return null;   // 每张让出一帧
        }

        // 强制重建布局
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(uploadScrollContent);

        UploadDebug($"布局刷新完成: childCount={uploadScrollContent.childCount}, created={createdCount}");

        uploadPanelCoroutine = null;
        UploadDebug("========== 上传 UI 刷新完成 ==========");
    }

    /// <summary>
    /// 点击"添加图片"按钮。
    /// 
    /// 【编辑器】用 EditorUtility.OpenFilePanel 打开文件选择框
    /// 【真机】用 NativeGallery.GetImageFromGallery 打开相册
    /// 
    /// 两种方式都会拿到图片路径，然后交给 SaveUploadedPng 保存。
    /// </summary>
    private void OnUploadButtonClicked()
    {
        ClearUploadDebug();
        UploadDebug("========== 点击上传图片 ==========");
        UploadDebug($"平台: {Application.platform}");
        UploadDebug($"persistentDataPath: {Application.persistentDataPath}");

#if UNITY_EDITOR
        // ---------- 编辑器：打开文件选择框 ----------
        UploadDebug("运行在 Unity Editor，打开文件选择器");
        string path = UnityEditor.EditorUtility.OpenFilePanel("选择图片", "", "png,jpg,jpeg");
        UploadDebug($"Editor 选择结果: {path}");

        if (!string.IsNullOrEmpty(path))
            StartCoroutine(ProcessUploadedImageAsync(path));
        else
            UploadDebug("用户取消选择");
#else
        // ---------- 真机：打开系统相册 ----------
        UploadDebug("调用 NativeGallery.GetImageFromGallery()");

        NativeGallery.GetImageFromGallery((path) =>
        {
            UploadDebug($"NativeGallery 回调 path = {path}");

            if (string.IsNullOrEmpty(path))
            {
                UploadDebug("用户取消选择，path 为空");
                return;
            }

            UploadDebug($"path.StartsWith(content://) = {path.StartsWith("content://")}");

            // 用 NativeGallery 直接加载为 Texture2D（它会处理各种图片格式）
            Texture2D texture = null;
            try
            {
                UploadDebug("开始 NativeGallery.LoadImageAtPath(path, 2048, false)");
                texture = NativeGallery.LoadImageAtPath(path, 2048, false);
            }
            catch (Exception e)
            {
                UploadDebug($"ERROR: LoadImageAtPath 异常: {e}");
                ShowConfirm("图片无法识别", null);
                return;
            }

            if (texture == null)
            {
                UploadDebug("ERROR: LoadImageAtPath 返回 null");
                ShowConfirm("图片无法识别，请换一张", null);
                return;
            }

            UploadDebug($"NativeGallery 图片加载成功: {texture.width}x{texture.height}");

            // 编码为 PNG 字节
            byte[] pngBytes = null;
            try
            {
                pngBytes = texture.EncodeToPNG();
                UploadDebug($"EncodeToPNG 完成: {pngBytes?.Length ?? 0} bytes");
            }
            catch (Exception e)
            {
                UploadDebug($"ERROR: EncodeToPNG 异常: {e}");
            }
            finally
            {
                Destroy(texture);   // 立即销毁临时纹理，避免显存占用
            }

            if (pngBytes == null || pngBytes.Length < 100)
            {
                UploadDebug("ERROR: PNG 编码结果无效");
                ShowConfirm("图片编码失败，请换一张", null);
                return;
            }

            UploadDebug("开始 StartCoroutine(SaveUploadedPng)");
            StartCoroutine(SaveUploadedPng(pngBytes));
        }, "选择图片", "image/*");
#endif
    }

    /// <summary>
    /// 从文件路径异步解码图片（编辑器 / content:// URI）。
    /// 用后台线程做 Android 原生解码（因为解码可能耗时）。
    /// </summary>
    private IEnumerator ProcessUploadedImageAsync(string sourcePath)
    {
        UploadDebug("========== ProcessUploadedImageAsync ==========");

        // 参数检查
        if (string.IsNullOrEmpty(sourcePath)) { UploadDebug("ERROR: sourcePath 为空"); ShowConfirm("上传失败:\nsourcePath 为空", null); yield break; }

        // 判断是 URI 还是普通文件路径
        bool isContentUri = sourcePath.StartsWith("content://");
        UploadDebug($"sourcePath = {sourcePath}");
        UploadDebug($"isContentUri = {isContentUri}");

        if (!isContentUri && !File.Exists(sourcePath))
        {
            UploadDebug("ERROR: source 文件不存在");
            ShowConfirm($"上传失败:\n文件不存在\n{sourcePath}", null);
            yield break;
        }

        // ---------- 后台线程解码 ----------
        byte[] pngBytes = null;
        bool decodingDone = false;
        string decodeError = null;

        // 用 Task.Run 把解码放到线程池，避免阻塞主线程
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                pngBytes = isContentUri
                    ? AndroidImageDecoder.DecodeUriToPngBytes(sourcePath)
                    : AndroidImageDecoder.DecodeToPngBytes(sourcePath);
            }
            catch (Exception e) { decodeError = e.ToString(); pngBytes = null; }
            finally { decodingDone = true; }
        });

        // 主线程轮询等待后台完成
        while (!decodingDone) yield return null;

        if (!string.IsNullOrEmpty(decodeError)) UploadDebug($"ERROR: 后台解码异常: {decodeError}");

        if (pngBytes == null || pngBytes.Length < 100)
        {
            string reason = AndroidImageDecoder.LastError;
            if (string.IsNullOrEmpty(reason)) reason = "解码返回空";
            UploadDebug($"ERROR: 解码失败: {reason}");
            ShowConfirm($"上传失败:\n{reason}", null);
            yield break;
        }

        UploadDebug($"后台解码成功: {pngBytes.Length} bytes");

        // 复用保存流程
        yield return SaveUploadedPng(pngBytes);
    }

    #endregion

    #region 局域网分享

    /// <summary>
    /// 点击【分享】按钮：启动服务端，等待客户端连接。
    /// </summary>
    private void OnShareButtonClicked()
    {
        UploadDebug("========== 点击【分享】 ==========");
        LANShareManager.Instance.StartSharing();     // 启动 TCP 服务器 + UDP 广播
        shareStatusText.text = "等待客户端连接...";
        StartCoroutine(WaitForClientConnection());   // 开始轮询等待连接
    }

    /// <summary>
    /// 轮询等待客户端连接，一旦连接就打开分享选择面板。
    /// </summary>
    private IEnumerator WaitForClientConnection()
    {
        UploadDebug("等待客户端连接...");
        while (!LANShareManager.Instance.ClientConnected)
            yield return null;

        UploadDebug("客户端已连接，打开分享选择面板");
        shareStatusText.text = "连接成功";
        OpenShareSelectPanel();
    }

    /// <summary>点击【接收】按钮：打开设备列表面板，开始搜索设备。</summary>
    private void OnReceiveButtonClicked()
    {
        UploadDebug("========== 点击【接收】 ==========");
        ShowPanel(deviceListPanel);
        StartDiscovery();
    }

    /// <summary>打开分享选择面板（清空上次选择 + 填充内容）。</summary>
    private void OpenShareSelectPanel()
    {
        shareSelectedFiles.Clear();   // 清空上次选择
        ShowPanel(shareSelectPanel);
        PopulateShareSelectPanel();
    }

    /// <summary>异步填充分享选择面板。</summary>
    private void PopulateShareSelectPanel()
    {
        StartCoroutine(PopulateShareSelectPanelAsync());
    }

    /// <summary>
    /// 实际填充分享选择面板的协程。
    /// 遍历本地所有上传图片，为每个创建可点击的缩略图。
    /// 点击切换"选中"状态（绿=已选，白=未选）。
    /// </summary>
    private IEnumerator PopulateShareSelectPanelAsync()
    {
        if (isSharePanelLoading) { Debug.Log("分享面板正在加载中"); yield break; }
        isSharePanelLoading = true;

        ReleaseDynamicSprites();

        // 清理旧按钮
        foreach (Transform child in shareSelectContent)
            Destroy(child.gameObject);

        // 网格布局：一行三列，每个 150×150
        GridLayoutGroup grid = shareSelectContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = shareSelectContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(150, 150);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        List<string> files = GameDataManager.GetUploadedImages();
        UploadDebug($"[分享面板] 可分享文件数: {files.Count}");

        foreach (string file in files)
        {
            string path = GameDataManager.GetUploadedImagePath(file);
            if (!File.Exists(path)) continue;

            Sprite sprite = null;
            yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => sprite = s);
            if (sprite == null) continue;

            dynamicSprites.Add(sprite);

            GameObject btnObj = Instantiate(imageButtonPrefab, shareSelectContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = sprite;

            // 用局部变量捕获，防止闭包陷阱
            string fileName = file;
            Image capturedImg = img;
            Button capturedBtn = btn;

            // 点击切换"选中"状态
            btn.onClick.AddListener(() =>
            {
                if (shareSelectedFiles.Contains(fileName))
                {
                    // 已选中 → 取消
                    shareSelectedFiles.Remove(fileName);
                    if (capturedImg != null) capturedImg.color = Color.white;
                }
                else
                {
                    // 未选中 → 选中（变绿）
                    shareSelectedFiles.Add(fileName);
                    if (capturedImg != null) capturedImg.color = Color.green;
                }
                UploadDebug($"[分享面板] 当前已选 {shareSelectedFiles.Count} 张");
            });

            yield return null;
        }

        isSharePanelLoading = false;
    }

    /// <summary>
    /// 服务端点击【分享】按钮。
    /// 把选中的文件推给 LANShareManager，并开始发就绪广播（不关闭面板）。
    /// 
    /// 【为什么保持面板打开？】
    /// 用户可能想再修改选择后重新分享。保持打开更灵活。
    /// </summary>
    private void StartSharingSelectedFiles()
    {
        if (shareSelectedFiles.Count == 0)
        {
            ShowConfirm("请至少选择一张图片", null);
            return;
        }

        UploadDebug($"========== 服务端点击【分享】，共 {shareSelectedFiles.Count} 张 ==========");
        foreach (var f in shareSelectedFiles)
            UploadDebug($"  - {f}");

        // 告诉 LANShareManager 要分享哪些文件
        LANShareManager.Instance.SetSharedFiles(shareSelectedFiles);

        // 开始周期性发送就绪广播（让客户端知道可以来下载了）
        LANShareManager.Instance.NotifyClientsReady();

        shareStatusText.text = $"已分享 {shareSelectedFiles.Count} 张，等待下载...";
        UploadDebug("已发送就绪广播，分享面板保持打开（可继续修改选择）");

        // 注意：不关闭 shareSelectPanel
    }

    /// <summary>
    /// 开始搜索设备（接收方）。
    /// 通过 LANShareManager 监听 UDP 广播，收到广播后回调处理。
    /// </summary>
    private void StartDiscovery()
    {
        if (deviceListContent == null) { UploadDebug("ERROR: deviceListContent 未赋值！"); return; }

        // 清理旧的设备按钮
        foreach (Transform child in deviceListContent)
            Destroy(child.gameObject);
        discoveredDevices.Clear();

        // 垂直布局（每个设备按钮占一行）
        VerticalLayoutGroup layout = deviceListContent.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            // 如果之前有其他布局组件，先移除（避免冲突）
            LayoutGroup existing = deviceListContent.GetComponent<LayoutGroup>();
            if (existing != null) Destroy(existing);
            layout = deviceListContent.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        // 启动发现（回调会在主线程执行，因为 LANShareManager 内部做了调度）
        LANShareManager.Instance.StartDiscovery((deviceName, ip, port, isReady) =>
        {
            UploadDebug($"【发现回调】device={deviceName}, ip={ip}, port={port}, isReady={isReady}");

            if (isReady)
            {
                // 收到就绪广播：可能服务端刚分享完，处于"可下载"状态
                UploadDebug($"【就绪广播】收到，IP={ip}");
                if (LANShareManager.Instance.ConnectedToServer)
                {
                    // 已经连接过 → 直接刷新列表
                    UploadDebug("【就绪广播】已连接状态，请求图片列表");
                    RequestRemoteImageList();
                }
                else
                {
                    // 未连接 → 添加设备按钮（标记为"可下载"）
                    string entry = $"{deviceName}|{ip}|{port}";
                    if (!discoveredDevices.Contains(entry))
                    {
                        discoveredDevices.Add(entry);
                        AddDeviceButton(deviceName, ip, port, true);
                    }
                    else
                    {
                        UploadDebug($"【就绪广播】设备已在列表中: {entry}");
                    }
                }
            }
            else
            {
                // 普通广播：设备存在但还没分享
                string entry = $"{deviceName}|{ip}|{port}";
                if (!discoveredDevices.Contains(entry))
                {
                    discoveredDevices.Add(entry);
                    AddDeviceButton(deviceName, ip, port, false);
                }
            }
        });
    }

    /// <summary>
    /// 请求远程图片列表（接收方）。
    /// 新流程不显示缩略图列表，只更新状态文字。
    /// </summary>
    private void RequestRemoteImageList()
    {
        UploadDebug("开始请求远程图片列表...");

        LANShareManager.Instance.DownloadImageList((list) =>
        {
            UploadDebug($"收到远程列表：{list.Count} 张");
            remoteImageFiles = list;

            if (list.Count == 0)
                remoteStatusText.text = "对方还没有分享图片，等待中...";
            else
                remoteStatusText.text = $"对方分享了 {list.Count} 张，点击【确认下载】";
        });
    }

    /// <summary>
    /// 为发现的设备创建一个按钮。
    /// 点击按钮尝试连接该设备。
    /// </summary>
    private void AddDeviceButton(string deviceName, string ip, int port, bool isReady)
    {
        GameObject btnObj = Instantiate(deviceButtonPrefab, deviceListContent);

        // 设置尺寸
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(deviceButtonWidth, deviceButtonHeight);

        Button btn = btnObj.GetComponent<Button>();
        Text label = btnObj.GetComponentInChildren<Text>();
        if (label != null)
        {
            // 就绪的设备加个"（可下载）"后缀
            string status = isReady ? "（可下载）" : "";
            label.text = $"{deviceName} ({ip}){status}";
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // 点击设备 → 尝试连接
        btn.onClick.AddListener(() =>
        {
            UploadDebug($"========== 点击设备: {deviceName} ({ip}:{port}) ==========");
            LANShareManager.Instance.ConnectToServer(ip, port);
            if (!LANShareManager.Instance.ConnectedToServer)
            {
                UploadDebug("连接失败");
                remoteStatusText.text = "连接失败";
                return;
            }

            connectedServerIP = ip;
            deviceListPanel.SetActive(false);
            remoteImagePanel.SetActive(true);
            remoteStatusText.text = "已连接，等待对方分享...";
            UploadDebug("连接成功，等待对方就绪广播");
        });
    }

    /// <summary>清空远程图片列表（新流程不再显示缩略图，仅清空备用）。</summary>
    private void PopulateRemoteImages()
    {
        foreach (Transform child in remoteImageContent)
            Destroy(child.gameObject);
    }

    /// <summary>切换远程图片的选中状态（新流程未使用，保留备用）。</summary>
    private void ToggleRemoteSelection(int index, GameObject btnObj)
    {
        if (selectedRemoteIndices.Contains(index))
        {
            selectedRemoteIndices.Remove(index);
            Image img = btnObj.GetComponent<Image>();
            if (img != null) img.color = Color.white;
            UploadDebug($"[远程] 取消选择: {remoteImageFiles[index]}（共 {selectedRemoteIndices.Count}）");
        }
        else
        {
            selectedRemoteIndices.Add(index);
            Image img = btnObj.GetComponent<Image>();
            if (img != null) img.color = Color.green;
            UploadDebug($"[远程] 选择: {remoteImageFiles[index]}（共 {selectedRemoteIndices.Count}）");
        }
    }

    /// <summary>
    /// 点击"确认下载"。
    /// 先请求一次列表确认最新状态，再逐个下载。
    /// </summary>
    private void DownloadSelectedImages()
    {
        UploadDebug("========== 点击【确认下载】 ==========");
        remoteStatusText.text = "正在获取列表...";

        LANShareManager.Instance.DownloadImageList((list) =>
        {
            remoteImageFiles = list;

            if (list.Count == 0)
            {
                remoteStatusText.text = "对方还没有分享图片";
                UploadDebug("远程列表为空");
                return;
            }

            UploadDebug($"========== 收到远程列表 {list.Count} 张 ==========");
            // 启动下载协程（逐个下载，每个之间让出帧）
            StartCoroutine(DownloadAllCoroutine(new List<string>(list)));
        });
    }

    /// <summary>
    /// 逐个下载所有远程图片。
    /// 
    /// 【为什么要让出帧？】
    /// DownloadImageToShared 是同步阻塞调用，一次性下载多张会卡 UI。
    /// 每个文件之间让出一帧 + 100ms 缓冲，保持界面流畅。
    /// </summary>
    private IEnumerator DownloadAllCoroutine(List<string> files)
    {
        int total = files.Count;
        int completed = 0;
        int failed = 0;

        for (int i = 0; i < total; i++)
        {
            string fn = files[i];
            UploadDebug($"[{i + 1}/{total}] 开始下载: {fn}");

            bool done = false;
            string savedPath = null;

            // 下载（同步调用，回调会在返回前执行）
            LANShareManager.Instance.DownloadImageToShared(fn, (path) =>
            {
                savedPath = path;
                done = true;
            });

            yield return null;   // 让出一帧，刷新 UI

            if (done && !string.IsNullOrEmpty(savedPath))
            {
                completed++;
                UploadDebug($"[{i + 1}/{total}] 成功: {savedPath}");
            }
            else
            {
                failed++;
                UploadDebug($"[{i + 1}/{total}] 失败");
            }

            // 更新状态文字
            remoteStatusText.text = $"进度 {i + 1}/{total}（成功 {completed}，失败 {failed}）";

            // 文件之间给 100ms 缓冲（让 socket 状态稳定）
            yield return new WaitForSeconds(0.1f);
        }

        remoteStatusText.text = $"下载完成：成功 {completed}/{total}，失败 {failed}";
        UploadDebug($"========== 下载结束：成功 {completed}，失败 {failed} ==========");
    }

    /// <summary>下载全部（备用方法，当前流程未使用）。</summary>
    private void DownloadAllRemoteImages()
    {
        int total = remoteImageFiles.Count;
        int completed = 0;
        UploadDebug($"========== 开始下载全部 {total} 张 ==========");

        foreach (var file in remoteImageFiles)
        {
            string fn = file;
            LANShareManager.Instance.DownloadImageToShared(fn, (path) =>
            {
                completed++;
                UploadDebug($"下载完成 {completed}/{total}: {path}");
                remoteStatusText.text = $"正在下载 {completed}/{total} 张...";

                if (completed >= total)
                {
                    remoteStatusText.text = $"下载完成，共 {total} 张已进入共享分类";
                    UploadDebug("全部下载完成");
                }
            });
        }
    }

    /// <summary>
    /// 分享停止事件处理（由 LANShareManager 触发）。
    /// </summary>
    private void HandleSharingStopped()
    {
        UploadDebug("【事件】OnSharingStopped 触发");
        if (shareStatusText != null) shareStatusText.text = "分享已停止";
    }

    #endregion

    #region 共享分类

    /// <summary>点击"共享"分类按钮：显示下载来的图片列表。</summary>
    private void OnSharedCategoryClicked()
    {
        selectedCategory = GameDataManager.SharedCategory;
        ShowPanel(sharedImagePanel);
        PopulateSharedImagePanel();
    }

    /// <summary>异步填充共享图片面板。</summary>
    private void PopulateSharedImagePanel()
    {
        StartCoroutine(PopulateSharedImagePanelAsync());
    }

    /// <summary>实际填充共享图片面板的协程。</summary>
    private IEnumerator PopulateSharedImagePanelAsync()
    {
        if (isSharedPanelLoading) { Debug.Log("共享面板正在加载中"); yield break; }
        isSharedPanelLoading = true;

        ReleaseDynamicSprites();

        foreach (Transform child in sharedImageContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = sharedImageContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = sharedImageContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 200);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        List<string> files = GameDataManager.GetSharedImages();
        UploadDebug($"[共享面板] 文件数: {files.Count}");

        foreach (string file in files)
        {
            string path = GameDataManager.GetSharedImagePath(file);
            if (!File.Exists(path)) continue;

            Sprite sprite = null;
            yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => sprite = s);
            if (sprite == null) continue;

            dynamicSprites.Add(sprite);

            GameObject btnObj = Instantiate(imageButtonPrefab, sharedImageContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = sprite;

            string fileName = file;

            // 长按删除
            LongPressHandler longPress = btnObj.GetComponent<LongPressHandler>();
            if (longPress == null) longPress = btnObj.AddComponent<LongPressHandler>();

            // 长按回调：弹出确认弹窗，确认后删除
            longPress.SetOnLongPress(() =>
            {
                UploadDebug($"[共享面板] 长按检测到: {fileName}");
                ShowConfirm($"是否删除这张共享图片？\n{fileName}", () =>
                {
                    GameDataManager.RemoveSharedImage(fileName);
                    UploadDebug($"[共享面板] 已删除: {fileName}");
                    // 刷新面板
                    PopulateSharedImagePanel();
                });
            });

            // 单击：进入难度选择（如果刚刚触发过长按，则忽略）
            btn.onClick.AddListener(() =>
            {
                if (LongPressHandler.ConsumeLongPressFlag()) return;
                OnSharedImageClicked(fileName);
            });

            yield return null;
        }

        isSharedPanelLoading = false;
    }

    /// <summary>
    /// 点击共享图片 → 进难度选择面板。
    /// </summary>
    private void OnSharedImageClicked(string fileName)
    {
        // 用共享列表的索引作为 selectedImageIndex
        selectedImageIndex = GameDataManager.GetSharedImages().IndexOf(fileName);
        ShowPanel(difficultyPanel);
    }

    #endregion

    #region 动态 Sprite 释放

    /// <summary>
    /// 释放所有动态创建的 Sprite 和其纹理。
    /// 
    /// 【为什么必须手动释放？】
    /// 通过 Sprite.Create 创建的 Sprite 和 Texture2D 不会自动被 GC 回收，
    /// 必须显式 Destroy，否则显存会越占越多（尤其反复打开上传/共享面板时）。
    /// 
    /// 【释放顺序】
    /// 先销毁 Texture（Sprite 依赖的底层资源），再销毁 Sprite。
    /// 反过来会导致 Unity 报错。
    /// </summary>
    private void ReleaseDynamicSprites()
    {
        if (dynamicSprites == null) return;

        foreach (var sprite in dynamicSprites)
        {
            if (sprite == null) continue;

            if (sprite.texture != null)
                Destroy(sprite.texture);   // 先销毁纹理

            Destroy(sprite);               // 再销毁 Sprite
        }

        dynamicSprites.Clear();
    }

    #endregion

    #region 广告与其他按钮

    /// <summary>
    /// 点击"看广告回体力"按钮。
    /// 真实项目需要接广告 SDK，这里用模拟的 ShowRewardedAd。
    /// </summary>
    private void OnAdStaminaClicked()
    {
        adStaminaButton.interactable = false;   // 防连点

        // 模拟广告：直接成功
        ShowRewardedAd(() =>
        {
            // 广告成功 → 加体力和金币
            GameDataManager.AddStamina(4);
            GameDataManager.AddCoins(2);
            UpdateStaminaDisplay();
            UpdateCoinDisplay();
            Debug.Log("观看广告成功，获得4体力、2金币");
            adStaminaButton.interactable = true;
        }, () =>
        {
            // 广告未完成
            Debug.Log("广告未完成，无奖励");
            adStaminaButton.interactable = true;
        });
    }

    /// <summary>
    /// 模拟广告（真实项目需替换为 SDK 调用）。
    /// 直接调用 onSuccess 表示"广告观看成功"。
    /// </summary>
    private void ShowRewardedAd(Action onSuccess, Action onFail)
    {
        onSuccess?.Invoke();
    }

    /// <summary>
    /// 点击"每日拼图"按钮。
    /// 检查今天是否已完成/已生成，然后切到游戏场景。
    /// </summary>
    private void OnDailyPuzzleClicked()
    {
        // 今天已完成 → 提示
        if (GameDataManager.IsDailyPuzzleCompletedToday())
        {
            ShowConfirm("今日每日拼图已完成，明天再来吧！", null);
            return;
        }

        // 没生成 → 生成新的
        if (!GameDataManager.IsDailyPuzzleGeneratedToday())
            GameDataManager.GenerateDailyPuzzle();

        // 标记为每日拼图模式，切到游戏场景
        PlayerPrefs.SetInt("IsDailyPuzzle", 1);
        PlayerPrefs.Save();
        SceneManager.LoadScene("GameScene");
    }

    #endregion

    #region 通用确认弹窗

    /// <summary>
    /// 显示通用确认弹窗。
    /// </summary>
    /// <param name="message">弹窗内容</param>
    /// <param name="onConfirm">点击"是"后的回调（可为 null）</param>
    private void ShowConfirm(string message, Action onConfirm)
    {
        confirmText.text = message;
        confirmAction = onConfirm;
        confirmPanel.SetActive(true);
        confirmPanel.transform.SetAsLastSibling();   // 放到最上层

        // 强制提升 Canvas 层级到最高
        // （确保弹窗能盖住所有其他 UI）
        Canvas confirmCanvas = confirmPanel.GetComponent<Canvas>();
        if (confirmCanvas == null)
        {
            confirmCanvas = confirmPanel.AddComponent<Canvas>();
            confirmPanel.AddComponent<GraphicRaycaster>();
        }
        confirmCanvas.overrideSorting = true;
        confirmCanvas.sortingOrder = 1000;
    }

    /// <summary>点击弹窗"是"按钮。</summary>
    private void OnConfirmYes()
    {
        confirmPanel.SetActive(false);
        confirmAction?.Invoke();   // 执行回调（如果非空）
    }

    #endregion

    #region 游戏启动

    /// <summary>
    /// 启动游戏场景。
    /// 
    /// 【核心流程】
    /// 1. 把选中的分类、难度、图片索引写入 PlayerPrefs
    /// 2. 播放加载音效
    /// 3. 显示加载面板
    /// 4. 异步加载 GameScene
    /// 
    /// 【为什么用 PlayerPrefs 传参？】
    /// 切场景会销毁所有物体，全局数据无法通过静态变量传递。
    /// PlayerPrefs 是跨场景的安全载体。
    /// </summary>
    private void StartGame(int gridSize)
    {
        // 写入参数
        PlayerPrefs.SetString("SelectedCategory", selectedCategory);
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.SetInt("SelectedImageIndex", selectedImageIndex);
        PlayerPrefs.Save();

        // 播放加载音效
        if (loadingSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlayLoadingSound(loadingSound);

        // 显示加载面板
        loadingPanel.SetActive(true);
        loadingPanel.transform.SetAsLastSibling();
        if (loadingText != null) loadingText.text = "加载中...";

        // 启动异步加载协程
        StartCoroutine(LoadGameAsync());
    }

    /// <summary>
    /// 异步加载游戏场景，带假进度条。
    /// 
    /// 【为什么是"假进度"？】
    /// asyncLoad.progress 最大只能到 0.9（Unity 保留 0.1 给激活场景用）。
    /// 而且实际加载可能很快（&lt;0.5 秒），进度条一闪而过，用户看不到。
    /// 所以这里用"实际进度和时间进度取最小值"：
    ///   - 时间进度：3 秒内从 0 走到 1
    ///   - 实际进度：Unity 内部进度
    ///   取两者的最小值 → 保证至少显示 3 秒的加载动画
    /// </summary>
    private IEnumerator LoadGameAsync()
    {
        float startTime = Time.realtimeSinceStartup;

        // 开始异步加载场景（不立即激活）
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("GameScene");
        asyncLoad.allowSceneActivation = false;   // 手动控制何时切换

        float displayProgress = 0f;
        float realProgress = 0f;

        while (displayProgress < 1f || asyncLoad.progress < 0.9f)
        {
            // 实际进度：归一化到 0~1
            realProgress = Mathf.Clamp01(asyncLoad.progress / 0.9f);

            // 时间进度：3 秒内从 0 到 1
            float timeProgress = Mathf.Clamp01((Time.realtimeSinceStartup - startTime) / 3f);

            // 取小值（保证进度条不会超过时间进度）
            displayProgress = Mathf.Min(realProgress, timeProgress);

            // 更新 UI
            if (loadingText != null)
                loadingText.text = $"加载中... {Mathf.RoundToInt(displayProgress * 100)}%";
            if (loadingSlider != null)
                loadingSlider.value = displayProgress;

            yield return null;
        }

        // 进度到 100%
        if (loadingText != null) loadingText.text = "加载中... 100%";

        // 停止加载音效
        if (SoundManager.Instance != null)
            SoundManager.Instance.StopLoadingSound();

        // 允许激活场景（触发实际切换）

        asyncLoad.allowSceneActivation = true;
    }

    #endregion

    #region 个人信息

    /// <summary>刷新金币显示。</summary>
    private void UpdateCoinDisplay()
    {
        if (coinText != null) coinText.text = "金币：" + GameDataManager.Coins;
    }

    /// <summary>点击"修改名字"按钮。</summary>
    private void OnChangeNameClicked()
    {
        panelAfterNameChange = profilePanel;
        nameInputField.text = GameDataManager.PlayerName;
        ShowPanel(nameInputPanel);
    }

    /// <summary>
    /// 确认修改名字。
    /// 
    /// 【校验规则】
    /// 1. 非空
    /// 2. 只能包含字母和数字（正则：^[a-zA-Z0-9]+$）
    /// </summary>
    private void OnNameConfirmed()
    {
        string name = nameInputField.text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ShowConfirm("名字不能为空", null);
            return;
        }

        // 正则验证：只允许 a-z A-Z 0-9
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9]+$"))
        {
            ShowConfirm("名字只能包含字母和数字", null);
            nameInputField.text = "";
            return;
        }

        // 保存名字
        confirmPanel.SetActive(false);
        GameDataManager.PlayerName = name;

        // 返回到改名字前的面板
        ShowPanel(panelAfterNameChange);
    }

    /// <summary>
    /// 刷新个人信息 UI：头像、名字、等级、经验条。
    /// </summary>
    private void UpdateProfileUI()
    {
        // ---------- 头像 ----------
        if (profileAvatarImage != null)
        {
            // 懒加载：第一次调用时从 Resources 加载所有头像
            if (avatarSprites == null || avatarSprites.Length == 0)
                avatarSprites = Resources.LoadAll<Sprite>("Art/HeadPicture");

            int avatarIndex = GameDataManager.GetAvatarIndex();
            if (avatarSprites != null && avatarSprites.Length > 0)
            {
                // 越界保护
                if (avatarIndex < 0 || avatarIndex >= avatarSprites.Length)
                    avatarIndex = 0;
                profileAvatarImage.sprite = avatarSprites[avatarIndex];
            }
            else
            {
                Debug.LogWarning("没有找到头像图片");
            }
        }

        // ---------- 名字 ----------
        if (profileNameText != null) profileNameText.text = GameDataManager.PlayerName;

        // ---------- 等级和经验 ----------
        if (profileLevelText != null)
            profileLevelText.text = $"等级 {GameDataManager.Level}  {GameDataManager.Experience}/{GameDataManager.GetRequiredExperience(GameDataManager.Level)}";

        if (experienceSlider != null)
        {
            experienceSlider.maxValue = GameDataManager.GetRequiredExperience(GameDataManager.Level);
            experienceSlider.value = GameDataManager.Experience;
            experienceSlider.interactable = false;
        }
    }

    #endregion

    #region 头像选择

    /// <summary>
    /// 打开头像选择面板，显示所有可选头像。
    /// 头像从 Resources/Art/HeadPicture 加载。
    /// </summary>
    private void OpenAvatarSelectPanel()
    {
        // 清理旧按钮
        foreach (Transform child in avatarScrollContent)
            Destroy(child.gameObject);

        // 网格布局
        GridLayoutGroup grid = avatarScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = avatarScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(150, 150);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 加载所有头像
        avatarSprites = Resources.LoadAll<Sprite>("Art/HeadPicture");
        if (avatarSprites.Length == 0)
        {
            Debug.LogWarning("No avatars found in Art/HeadPicture");
            return;
        }

        // 按名字排序，保证每次显示顺序一致
        Array.Sort(avatarSprites, (a, b) => string.Compare(a.name, b.name));

        // 为每个头像创建按钮
        for (int i = 0; i < avatarSprites.Length; i++)
        {
            GameObject btnObj = Instantiate(avatarButtonPrefab, avatarScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = avatarSprites[i];

            // 闭包捕获
            int index = i;
            btn.onClick.AddListener(() => OnAvatarClicked(index));
        }

        // 滚动到顶部
        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = avatarSelectPanel.GetComponentInChildren<ScrollRect>();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;

        ShowPanel(avatarSelectPanel);
    }

    /// <summary>点击某个头像：保存选择并返回个人信息面板。</summary>
    private void OnAvatarClicked(int index)
    {
        GameDataManager.SetAvatarIndex(index);
        UpdateProfileUI();
        ShowPanel(profilePanel);
    }

    #endregion

    #region 收藏面板

    /// <summary>
    /// 填充收藏面板：显示所有已收藏的图片。
    /// 
    /// 【收藏数据格式】
    /// GameDataManager 里存的是 "分类名_图片索引" 字符串列表。
    /// 这里要解析出来，从 AssetBundle 里找到对应的 Sprite 显示。
    /// </summary>
    private void PopulateFavoritesPanel()
    {
        foreach (Transform child in favoritesScrollContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = favoritesScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = favoritesScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(150, 150);
        grid.spacing = new Vector2(10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        List<string> favorites = GameDataManager.GetFavorites();

        foreach (string fav in favorites)
        {
            // fav 格式："分类_索引"，拆开
            string[] parts = fav.Split('_');
            if (parts.Length != 2) continue;

            string category = parts[0];
            int imageIndex;
            if (!int.TryParse(parts[1], out imageIndex)) continue;

            // 从 AB 里拿该分类的所有 Sprite
            Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites(category);
            Array.Sort(sprites, (a, b) => string.Compare(a.name, b.name));
            if (imageIndex < 0 || imageIndex >= sprites.Length) continue;

            // 创建收藏按钮
            GameObject btnObj = Instantiate(imageButtonPrefab, favoritesScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            Text label = btnObj.GetComponentInChildren<Text>();

            bool unlocked = GameDataManager.IsImageUnlocked(category, imageIndex);

            if (img != null)
            {
                img.sprite = sprites[imageIndex];
                // 未解锁显示半透明灰
                img.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.7f);
            }
            if (label != null)
                label.text = unlocked ? "" : $"{GameDataManager.GetImagePrice(category, imageIndex)}金币";

            // 闭包捕获
            string cat = category;
            int idx = imageIndex;

            btn.onClick.AddListener(() =>
            {
                if (GameDataManager.IsImageUnlocked(cat, idx))
                {
                    // 已解锁 → 进难度面板
                    selectedCategory = cat;
                    selectedImageIndex = idx;
                    ShowPanel(difficultyPanel);
                }
                else
                {
                    // 未解锁 → 弹购买
                    pendingPurchaseCategory = cat;
                    pendingPurchaseImageIndex = idx;
                    purchaseText.text = $"是否花费 {GameDataManager.GetImagePrice(cat, idx)} 金币解锁这张图片？";
                    ShowPanel(purchasePanel);
                }
            });
        }
    }

    #endregion
}