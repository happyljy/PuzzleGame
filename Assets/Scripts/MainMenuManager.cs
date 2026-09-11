using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// 主菜单管理器：负责主菜单所有 UI 面板的切换、分类按钮生成、图片选择、
/// 个人信息、收藏、每日拼图、看广告恢复体力、购买、上传图片管理、
/// 局域网分享（手动连接、多选下载、共享分类）等功能。
/// 
/// 图片加载统一使用 ImageLoader.LoadSpriteFromFileAsync 进行后台线程解码。
/// 同时包含：
/// - 防重入：各面板加载期间忽略新的加载请求。
/// - 纹理释放：切换面板时释放动态创建的上传/共享 Sprite。
/// - 局域网分享全流程实时 Debug，输出到 uploadDebugText。
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    #region 单例（供 LANShareManager 输出日志）

    /// <summary>当前主菜单实例。仅主菜单场景有效。</summary>
    public static MainMenuManager Instance { get; private set; }

    #endregion

    #region UI 引用 - 设置面板

    [Header("设置面板")]
    public GameObject settingsPanel;
    public Button settingsButton;
    public Button settingsCloseButton;
    public Button stopBGMButton;
    public Slider bgmSlider;
    public Slider sfxSlider;

    [Header("退出游戏")]
    public Button quitGameButton;

    [Header("加载音效")]
    public AudioClip loadingSound;

    #endregion

    #region UI 引用 - 局域网分享

    [Header("停止广播")]
    public Button stopBroadcastButton;

    [Header("局域网分享 UI")]
    public GameObject deviceListPanel;
    public RectTransform deviceListContent;
    public GameObject deviceButtonPrefab;
    public float deviceButtonWidth = 620f;
    public float deviceButtonHeight = 100f;
    public Button cancelDiscoverButton;

    public GameObject remoteImagePanel;
    public RectTransform remoteImageContent;
    public GameObject remoteImageButtonPrefab;
    public Button cancelConnectButton;
    public Button downloadSelectedButton;
    public Text remoteStatusText;

    public GameObject shareSelectPanel;
    public RectTransform shareSelectContent;
    public Button startSharingButton;
    public Button cancelShareSelectButton;

    [Header("局域网分享")]
    public Button shareButton;
    public Button receiveButton;
    public Text shareStatusText;

    [Header("共享分类")]
    public GameObject sharedImagePanel;
    public RectTransform sharedImageContent;
    public Button sharedCloseButton;

    #endregion

    #region UI 引用 - 个人信息

    [Header("修改名字")]
    public Button changeNameButton;

    [Header("换头像")]
    public Button changeAvatarButton;
    public GameObject avatarSelectPanel;
    public RectTransform avatarScrollContent;
    public GameObject avatarButtonPrefab;
    public Button avatarCloseButton;
    public Image profileAvatarImage;

    [Header("上传图片")]
    public Button uploadButtonInProfile;
    public GameObject uploadManagePanel;
    public RectTransform uploadScrollContent;
    public Button uploadCloseButton;
    public Button addImageButton;

    [Header("个人信息")]
    public GameObject profilePanel;
    public Text profileNameText;
    public Text profileLevelText;
    public Slider experienceSlider;
    public Button favoritesButton;

    [Header("姓名输入面板")]
    public GameObject nameInputPanel;
    public InputField nameInputField;
    public Button nameConfirmButton;

    [Header("我的收藏面板")]
    public GameObject favoritesPanel;
    public RectTransform favoritesScrollContent;
    public Button favoritesCloseButton;

    #endregion

    #region UI 引用 - 分类与图片

    [Header("分类 ScrollView")]
    public GameObject categoryScrollView;
    public RectTransform categoryScrollContent;
    public GameObject categoryButtonPrefab;

    [Header("分类预览设置")]
    public float previewChangeInterval = 3f;
    public float fadeDuration = 0.5f;

    [Header("图片选择面板")]
    public GameObject imageSelectPanel;
    public Button closeImagePanelButton;
    public RectTransform imageScrollContent;
    public GameObject imageButtonPrefab;

    [Header("难度面板")]
    public GameObject difficultyPanel;
    public Button easyButton;
    public Button normalButton;
    public Button hardButton;
    public Button difficultyCancelButton;

    [Header("购买面板")]
    public GameObject purchasePanel;
    public Text purchaseText;
    public Button confirmPurchaseButton;
    public Button cancelPurchaseButton;

    #endregion

    #region UI 引用 - 通用

    [Header("每日拼图")]
    public Button dailyPuzzleButton;

    [Header("加载面板")]
    public GameObject loadingPanel;
    public Text loadingText;
    public Slider loadingSlider;

    [Header("上传实时 Debug")]
    [Tooltip("把上传全过程日志显示到主菜单的 Text 上，方便真机排查。")]
    public Text uploadDebugText;
    [Tooltip("Debug Text 最多保留多少行。")]
    public int uploadDebugMaxLines = 80;

    [Header("通用确认弹窗")]
    public GameObject confirmPanel;
    public Text confirmText;
    public Button confirmYesButton;
    public Button confirmNoButton;

    [Header("广告恢复")]
    public Button adStaminaButton;

    [Header("体力显示")]
    public Text staminaText;
    public Slider staminaSlider;

    [Header("金币显示")]
    public Text coinText;

    [Header("底部按钮")]
    public Button profileButton;
    public Button categoryButton;

    #endregion

    #region 私有状态

    private bool isFlashing = false;
    private string selectedCategory;
    private int selectedImageIndex = -1;
    private string pendingPurchaseCategory;
    private int pendingPurchaseImageIndex;

    // 面板层级管理
    private GameObject currentBasePanel;
    private GameObject currentLayer2Panel;
    private GameObject currentLayer3Panel;
    private GameObject currentLayer4Panel;

    // 预览图协程缓存
    private Dictionary<Button, Coroutine> previewCoroutines = new Dictionary<Button, Coroutine>();

    private Action confirmAction;
    private GameObject panelAfterNameChange;
    private Sprite[] avatarSprites;

    // 局域网分享状态
    private List<string> discoveredDevices = new List<string>();
    private List<string> remoteImageFiles = new List<string>();
    private HashSet<int> selectedRemoteIndices = new HashSet<int>();
    private List<string> shareSelectedFiles = new List<string>();
    private string connectedServerIP = null;


    // ★ 防重入标志
    private bool isImagePanelLoading = false;    // 图片选择面板

    private bool isSharePanelLoading = false;    // 分享选择面板
    private bool isSharedPanelLoading = false;   // 共享分类面板

    // 上传管理面板当前刷新协程，用于强制刷新时取消旧协程
    private Coroutine uploadPanelCoroutine = null;

    // ★ 动态创建的 Sprite 列表（上传/共享来源），用于面板切换时释放
    private List<Sprite> dynamicSprites = new List<Sprite>();

    // 上传实时 Debug 行
    private readonly List<string> uploadDebugLines = new List<string>();

    #endregion

    #region 上传实时 Debug

    /// <summary>
    /// 追加一行 Debug 日志到 uploadDebugText（同时输出到控制台）。
    /// 该方法为 public，供 LANShareManager 等其他模块调用。
    /// </summary>
    public void UploadDebug(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Debug.Log(line);

        if (uploadDebugText == null)
            return;

        uploadDebugLines.Add(line);

        int maxLines = Mathf.Max(10, uploadDebugMaxLines);
        while (uploadDebugLines.Count > maxLines)
            uploadDebugLines.RemoveAt(0);

        uploadDebugText.text = string.Join("\n", uploadDebugLines);
    }

    private void ClearUploadDebug()
    {
        uploadDebugLines.Clear();
        if (uploadDebugText != null)
            uploadDebugText.text = "";
    }

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        Instance = this;
        // GameDataManager.ResetForEditor(); // 测试用
    }

    private void Start()
    {
        // ★ 启动时清空并打印一条
        ClearUploadDebug();
        UploadDebug("========== 主菜单启动 ==========");

        LANShareManager.Instance.OnSharingStopped += HandleSharingStopped;

        StartCoroutine(WaitForAssetBundle());

        // ========== 设置面板 ==========
        settingsButton.onClick.AddListener(OpenSettingsPanel);
        settingsCloseButton.onClick.AddListener(() => ShowPanel(profilePanel));
        stopBGMButton.onClick.AddListener(ToggleBGM);
        UpdateBGMButtonText();

        bgmSlider.minValue = 0f;
        bgmSlider.maxValue = 1f;
        bgmSlider.value = SoundManager.Instance != null ? SoundManager.Instance.BGMVolume : 0.5f;
        bgmSlider.onValueChanged.AddListener((v) =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetBGMVolume(v);
        });

        sfxSlider.minValue = 0f;
        sfxSlider.maxValue = 1f;
        sfxSlider.value = SoundManager.Instance != null ? SoundManager.Instance.SFXVolume : 1f;
        sfxSlider.onValueChanged.AddListener((v) =>
        {
            if (SoundManager.Instance != null) SoundManager.Instance.SetSFXVolume(v);
        });

        quitGameButton.onClick.AddListener(QuitGame);

        // ========== 停止广播 ==========
        stopBroadcastButton.onClick.AddListener(() =>
        {
            UploadDebug("========== 点击【停止广播】 ==========");
            LANShareManager.Instance.StopSharing();
            LANShareManager.Instance.DisconnectFromServer();
            shareStatusText.text = "连接已断开";
            ShowPanel(currentBasePanel ?? profilePanel);
        });

        // ========== 局域网分享 ==========
        shareButton.onClick.AddListener(OnShareButtonClicked);
        receiveButton.onClick.AddListener(OnReceiveButtonClicked);

        cancelDiscoverButton.onClick.AddListener(() =>
        {
            UploadDebug("========== 取消发现 ==========");
            LANShareManager.Instance.StopDiscovery();
            ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        cancelConnectButton.onClick.AddListener(() =>
        {
            UploadDebug("========== 取消连接 ==========");
            LANShareManager.Instance.DisconnectFromServer();
            ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        downloadSelectedButton.onClick.AddListener(DownloadSelectedImages);
        startSharingButton.onClick.AddListener(StartSharingSelectedFiles);
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

        profileButton.onClick.AddListener(() => ShowPanel(profilePanel));
        categoryButton.onClick.AddListener(() => ShowPanel(categoryScrollView));

        sharedCloseButton.onClick.AddListener(() => ShowPanel(categoryScrollView));

        difficultyCancelButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null) ShowPanel(currentLayer2Panel);
            else ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        // ========== 体力与初始面板 ==========
        GameDataManager.InitStaminaSystem();
        StartCoroutine(UpdateStaminaUI());

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
        easyButton.onClick.AddListener(() => StartGame(2));
        normalButton.onClick.AddListener(() => StartGame(8));
        hardButton.onClick.AddListener(() => StartGame(10));

        // ========== 广告与购买 ==========
        adStaminaButton.onClick.AddListener(OnAdStaminaClicked);

        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
        cancelPurchaseButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null) ShowPanel(currentLayer2Panel);
            else ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        closeImagePanelButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? categoryScrollView));

        // ========== 初始化 ==========
        UpdateCoinDisplay();
        loadingPanel.SetActive(false);

        avatarSelectPanel.SetActive(false);
        shareSelectPanel.SetActive(false);
        deviceListPanel.SetActive(false);
        remoteImagePanel.SetActive(false);
        sharedImagePanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    private void OnEnable()
    {
        UpdateCoinDisplay();
        UpdateProfileUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (LANShareManager.Instance != null)
            LANShareManager.Instance.OnSharingStopped -= HandleSharingStopped;

        // 场景销毁时释放动态纹理
        ReleaseDynamicSprites();
    }

    #endregion

    #region AssetBundle 等待

    private IEnumerator WaitForAssetBundle()
    {
        while (AssetBundleManager.Instance == null || !AssetBundleManager.Instance.IsLoaded)
            yield return null;

        GenerateCategoryButtons();
        UpdateCoinDisplay();
        UpdateProfileUI();
    }

    #endregion

    #region 面板管理

    private void ShowPanel(GameObject panelToShow)
    {
        if (panelToShow == null)
        {
            HideOverlayPanels();
            confirmPanel.SetActive(false);
            return;
        }

        confirmPanel.SetActive(false);

        // 基础面板
        if (panelToShow == categoryScrollView || panelToShow == profilePanel)
        {
            HideOverlayPanels();
            categoryScrollView.SetActive(panelToShow == categoryScrollView);
            profilePanel.SetActive(panelToShow == profilePanel);
            currentBasePanel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        // 第二层面板
        else if (panelToShow == imageSelectPanel || panelToShow == favoritesPanel ||
                 panelToShow == uploadManagePanel || panelToShow == avatarSelectPanel ||
                 panelToShow == shareSelectPanel || panelToShow == deviceListPanel ||
                 panelToShow == remoteImagePanel || panelToShow == sharedImagePanel ||
                 panelToShow == settingsPanel)
        {
            HidePanelsAboveLayer2();

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
        // 第三层面板
        else if (panelToShow == difficultyPanel)
        {
            HidePanelsAboveLayer3();
            difficultyPanel.SetActive(true);
            currentLayer3Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        // 第四层面板
        else if (panelToShow == purchasePanel)
        {
            HidePanelsAboveLayer4();
            purchasePanel.SetActive(true);
            currentLayer4Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        // 姓名输入面板
        else if (panelToShow == nameInputPanel)
        {
            HideAllPanels();
            nameInputPanel.SetActive(true);
            nameInputPanel.transform.SetAsLastSibling();

            Canvas canvas = nameInputPanel.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = 999;
            }
        }

        if (panelToShow == profilePanel) UpdateProfileUI();
        if (panelToShow == favoritesPanel) PopulateFavoritesPanel();
        if (panelToShow == uploadManagePanel) PopulateUploadManagePanel();
    }

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

        currentLayer2Panel = null;
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

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

    private void HidePanelsAboveLayer3()
    {
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentLayer4Panel = null;
    }

    private void HidePanelsAboveLayer4()
    {
        nameInputPanel.SetActive(false);
    }

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

        currentBasePanel = null;
        currentLayer2Panel = null;
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    #endregion

    #region 设置面板

    private void OpenSettingsPanel()
    {
        if (SoundManager.Instance != null)
        {
            bgmSlider.value = SoundManager.Instance.BGMVolume;
            sfxSlider.value = SoundManager.Instance.SFXVolume;
        }
        ShowPanel(settingsPanel);
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ToggleBGM()
    {
        if (SoundManager.Instance == null) return;

        if (SoundManager.Instance.IsBGMPlaying)
            SoundManager.Instance.StopBGM();
        else
            SoundManager.Instance.PlayBGM();

        UpdateBGMButtonText();
    }

    private void UpdateBGMButtonText()
    {
        if (stopBGMButton == null) return;

        Text label = stopBGMButton.GetComponentInChildren<Text>();
        if (label == null) return;

        bool isPlaying = SoundManager.Instance != null && SoundManager.Instance.IsBGMPlaying;
        label.text = isPlaying ? "停止背景音乐" : "播放背景音乐";
    }

    #endregion

    #region 分类按钮生成

    private void GenerateCategoryButtons()
    {
        foreach (var kvp in previewCoroutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        previewCoroutines.Clear();

        foreach (Transform child in categoryScrollContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = categoryScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = categoryScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(350, 350);
        grid.spacing = new Vector2(50, 50);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 普通分类按钮
        for (int i = 0; i < GameDataManager.Categories.Length; i++)
        {
            string category = GameDataManager.Categories[i];
            GameObject btnObj = Instantiate(categoryButtonPrefab, categoryScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Text label = btnObj.GetComponentInChildren<Text>();
            Image previewImage = btnObj.transform.Find("PreviewImage")?.GetComponent<Image>();

            if (label != null) label.text = category;

            int index = i;
            btn.onClick.AddListener(() => OnCategoryClicked(index));

            if (previewImage != null)
            {
                Coroutine coroutine = StartCoroutine(UpdateCategoryPreview(previewImage, category));
                previewCoroutines[btn] = coroutine;
            }
        }

        // 上传分类按钮
        GameObject uploadBtnObj = Instantiate(categoryButtonPrefab, categoryScrollContent);
        Button uploadBtn = uploadBtnObj.GetComponent<Button>();
        Text uploadLabel = uploadBtnObj.GetComponentInChildren<Text>();
        Image uploadPreview = uploadBtnObj.transform.Find("PreviewImage")?.GetComponent<Image>();
        if (uploadLabel != null) uploadLabel.text = "上传";
        if (uploadPreview != null) uploadPreview.sprite = null;
        uploadBtn.onClick.AddListener(() => OnCategoryClicked(GameDataManager.Categories.Length));

        // 共享分类按钮
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

    private IEnumerator UpdateStaminaUI()
    {
        while (true)
        {
            UpdateStaminaDisplay();
            yield return new WaitForSeconds(1f);
        }
    }

    private void UpdateStaminaDisplay()
    {
        if (staminaText != null)
            staminaText.text = $"体力：{GameDataManager.Stamina}/{GameDataManager.MaxStamina}";

        if (staminaSlider != null)
        {
            staminaSlider.maxValue = GameDataManager.MaxStamina;
            staminaSlider.value = GameDataManager.Stamina;
            staminaSlider.interactable = false;
        }
    }

    #endregion

    #region 分类预览

    private IEnumerator UpdateCategoryPreview(Image previewImage, string category)
    {
        if (previewImage == null) yield break;

        Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites(category);
        if (sprites.Length == 0) yield break;

        while (previewImage != null)
        {
            Sprite newSprite = sprites[Random.Range(0, sprites.Length)];
            yield return StartCoroutine(FadeToSprite(previewImage, newSprite));
            if (previewImage == null) yield break;
            yield return new WaitForSeconds(previewChangeInterval);
        }
    }

    private IEnumerator FadeToSprite(Image image, Sprite newSprite)
    {
        if (image == null) yield break;

        float elapsed = 0f;
        Color startColor = image.color;
        Color transparentColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        while (elapsed < fadeDuration)
        {
            if (image == null) yield break;
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(startColor, transparentColor, elapsed / fadeDuration);
            yield return null;
        }

        if (image == null) yield break;
        image.sprite = newSprite;

        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            if (image == null) yield break;
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(transparentColor, startColor, elapsed / fadeDuration);
            yield return null;
        }

        if (image != null) image.color = startColor;
    }

    #endregion

    #region 分类点击与图片选择

    private void OnCategoryClicked(int index)
    {
        if (index == GameDataManager.Categories.Length)
        {
            selectedCategory = GameDataManager.UploadCategory;
            ShowPanel(imageSelectPanel);
            StartCoroutine(OpenImageSelectPanelAsync(GameDataManager.UploadCategory));
            return;
        }

        string category = GameDataManager.Categories[index];
        selectedCategory = category;
        ShowPanel(imageSelectPanel);
        StartCoroutine(OpenImageSelectPanelAsync(category));
    }

    /// <summary>
    /// 异步填充图片选择面板（防重入 + 释放旧动态 Sprite）。
    /// </summary>
    private IEnumerator OpenImageSelectPanelAsync(string category)
    {
        if (isImagePanelLoading)
        {
            Debug.Log("图片面板正在加载中，忽略本次请求");
            yield break;
        }
        isImagePanelLoading = true;

        // 释放上一次的动态 Sprite
        ReleaseDynamicSprites();

        foreach (Transform child in imageScrollContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = imageScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = imageScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(300, 300);
        grid.spacing = new Vector2(40, 40);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 上传分类：从文件系统异步加载
        if (category == GameDataManager.UploadCategory)
        {
            List<string> files = GameDataManager.GetUploadedImages();
            for (int i = 0; i < files.Count; i++)
            {
                string path = GameDataManager.GetUploadedImagePath(files[i]);
                if (!File.Exists(path)) continue;

                Sprite sprite = null;
                yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => sprite = s);
                if (sprite == null) continue;

                dynamicSprites.Add(sprite);
                CreateImageButton(category, i, sprite, files[i]);

                // 每张让出一帧，保持 UI 流畅
                yield return null;
            }
        }
        // 普通分类：从 AssetBundle 同步加载
        else
        {
            Sprite[] loadedSprites = AssetBundleManager.Instance.GetCategorySprites(category);
            Array.Sort(loadedSprites, (a, b) => string.Compare(a.name, b.name));

            for (int i = 0; i < loadedSprites.Length; i++)
            {
                CreateImageButton(category, i, loadedSprites[i], loadedSprites[i].name);
            }
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(imageScrollContent);

        isImagePanelLoading = false;
    }

    /// <summary>
    /// 创建单个图片按钮（用于图片选择面板）。
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
            if (label != null) label.text = "";
        }
        else
        {
            bool unlocked = GameDataManager.IsImageUnlocked(category, imageIndex);
            if (img != null)
                img.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.7f);
            if (label != null)
                label.text = unlocked ? "" : $"{GameDataManager.GetImagePrice(category, imageIndex)}金币";
        }

        int idx = imageIndex;
        imgBtn.onClick.AddListener(() => OnImageClicked(idx));
    }

    private void OnImageClicked(int imageIndex)
    {
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            selectedImageIndex = imageIndex;
            ShowPanel(difficultyPanel);
            return;
        }

        if (imageIndex == -1)
        {
            selectedImageIndex = -1;
            ShowPanel(difficultyPanel);
        }
        else
        {
            string category = selectedCategory;
            if (GameDataManager.IsImageUnlocked(category, imageIndex))
            {
                selectedImageIndex = imageIndex;
                ShowPanel(difficultyPanel);
            }
            else
            {
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

    private void ConfirmPurchase()
    {
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
        else
        {
            int price = GameDataManager.GetImagePrice(pendingPurchaseCategory, pendingPurchaseImageIndex);
            if (GameDataManager.SpendCoins(price))
            {
                GameDataManager.UnlockImage(pendingPurchaseCategory, pendingPurchaseImageIndex);
                UpdateCoinDisplay();
                ShowPanel(imageSelectPanel);
                StartCoroutine(OpenImageSelectPanelAsync(pendingPurchaseCategory));
                Debug.Log($"解锁图片 {pendingPurchaseCategory}_{pendingPurchaseImageIndex} 成功！");
            }
            else
            {
                purchaseText.text = "金币不足！";
                StartCoroutine(FlashCoinTextRed());
            }
        }
    }

    private IEnumerator FlashCoinTextRed()
    {
        if (coinText == null || isFlashing) yield break;

        isFlashing = true;
        Color originalColor = coinText.color;

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

    private void OpenUploadManagePanel()
    {
        UploadDebug("打开上传管理面板");
        ShowPanel(uploadManagePanel);
    }

    /// <summary>
    /// 刷新上传管理面板。
    /// 如果旧刷新还在进行，则先取消旧协程，再重新读取最新的上传列表。
    /// </summary>
    private void PopulateUploadManagePanel()
    {
        UploadDebug("请求刷新上传管理面板");

        if (uploadPanelCoroutine != null)
        {
            UploadDebug("发现旧的上传 UI 刷新协程，停止旧协程");
            StopCoroutine(uploadPanelCoroutine);
            uploadPanelCoroutine = null;
        }

        if (uploadManagePanel == null)
        {
            UploadDebug("ERROR: uploadManagePanel 未绑定");
            return;
        }

        if (uploadScrollContent == null)
        {
            UploadDebug("ERROR: uploadScrollContent 未绑定");
            return;
        }

        if (imageButtonPrefab == null)
        {
            UploadDebug("ERROR: imageButtonPrefab 未绑定");
            return;
        }

        uploadPanelCoroutine = StartCoroutine(PopulateUploadManagePanelAsync());
    }

    /// <summary>
    /// 将 PNG 字节保存到 Uploads 目录，写入 PlayerPrefs 后立即刷新上传 UI。
    /// </summary>
    private IEnumerator SaveUploadedPng(byte[] pngBytes)
    {
        UploadDebug("进入 SaveUploadedPng()");

        if (pngBytes == null)
        {
            UploadDebug("ERROR: pngBytes == null");
            ShowConfirm("上传失败:\nPNG 编码失败", null);
            yield break;
        }

        UploadDebug($"PNG bytes = {pngBytes.Length}");

        if (pngBytes.Length < 100)
        {
            UploadDebug("ERROR: PNG 字节长度 < 100");
            ShowConfirm("上传失败:\nPNG 编码失败", null);
            yield break;
        }

        // 1. 在内存中验证 PNG
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

        if (testTex != null)
            Destroy(testTex);

        // 2. 写入 Uploads
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

        // 3. 写入数据列表
        try
        {
            GameDataManager.AddUploadedImage(fileName);
            List<string> savedFiles = GameDataManager.GetUploadedImages();
            UploadDebug($"PlayerPrefs 写入成功，当前上传列表数量: {savedFiles.Count}");

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

        // 4. 刷新分类按钮
        if (categoryScrollView != null && categoryScrollView.activeSelf)
        {
            UploadDebug("当前分类面板可见，刷新分类按钮");
            GenerateCategoryButtons();
        }
        else
        {
            UploadDebug("分类面板当前不可见，跳过分类按钮刷新");
        }

        // 5. 刷新上传管理 UI
        UploadDebug("开始刷新上传管理 UI");
        PopulateUploadManagePanel();

        yield return null;

        UploadDebug("SaveUploadedPng() 完成");
    }

    private IEnumerator PopulateUploadManagePanelAsync()
    {
        UploadDebug("========== 开始刷新上传 UI ==========");

        if (uploadScrollContent == null)
        {
            UploadDebug("ERROR: uploadScrollContent == null");
            uploadPanelCoroutine = null;
            yield break;
        }

        if (imageButtonPrefab == null)
        {
            UploadDebug("ERROR: imageButtonPrefab == null");
            uploadPanelCoroutine = null;
            yield break;
        }

        // 清理旧动态 Sprite
        UploadDebug($"清理 dynamicSprites，当前数量={dynamicSprites.Count}");
        ReleaseDynamicSprites();

        // 清理旧按钮
        int oldChildCount = uploadScrollContent.childCount;
        UploadDebug($"清理旧上传按钮，childCount={oldChildCount}");
        foreach (Transform child in uploadScrollContent)
            Destroy(child.gameObject);

        yield return null;

        GridLayoutGroup grid = uploadScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
            grid = uploadScrollContent.gameObject.AddComponent<GridLayoutGroup>();

        grid.cellSize = new Vector2(200, 200);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter fitter = uploadScrollContent.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = uploadScrollContent.gameObject.AddComponent<ContentSizeFitter>();

        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        List<string> files = GameDataManager.GetUploadedImages();
        UploadDebug($"从 GameDataManager 读取上传列表: count={files.Count}");

        if (files.Count == 0)
        {
            UploadDebug("当前没有任何上传图片记录");
        }

        int createdCount = 0;

        foreach (string file in files)
        {
            if (string.IsNullOrEmpty(file))
            {
                UploadDebug("WARNING: 上传列表里出现空文件名，跳过");
                continue;
            }

            string path = GameDataManager.GetUploadedImagePath(file);
            bool exists = File.Exists(path);
            UploadDebug($"检查[{createdCount}] file={file}, exists={exists}");

            if (!exists)
            {
                UploadDebug($"WARNING: 文件不存在，跳过: {path}");
                continue;
            }

            long length = 0;
            try { length = new FileInfo(path).Length; } catch { }
            UploadDebug($"文件大小: {length} bytes");

            Sprite sprite = null;
            yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => sprite = s);

            if (sprite == null)
            {
                UploadDebug($"ERROR: ImageLoader 创建 Sprite 失败: {file}");
                continue;
            }

            UploadDebug($"Sprite 创建成功: {sprite.name}, {sprite.texture.width}x{sprite.texture.height}");
            dynamicSprites.Add(sprite);

            GameObject btnObj = null;
            try
            {
                btnObj = Instantiate(imageButtonPrefab, uploadScrollContent);
            }
            catch (Exception e)
            {
                UploadDebug($"ERROR: Instantiate imageButtonPrefab 异常: {e}");
                continue;
            }

            if (btnObj == null)
            {
                UploadDebug("ERROR: Instantiate 返回 null");
                continue;
            }

            Button btn = btnObj.GetComponent<Button>();
            if (btn == null)
            {
                UploadDebug($"WARNING: 图片按钮 Prefab 上没有 Button: {btnObj.name}");
            }

            // 优先查找名为 Image 的子节点；找不到就自动找第一个 Image
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img == null)
                img = btnObj.GetComponentInChildren<Image>(true);

            UploadDebug($"按钮创建成功: {btnObj.name}, Image组件存在={img != null}");

            if (img != null)
            {
                img.enabled = true;
                img.sprite = sprite;
                img.color = Color.white;
                UploadDebug($"Image 已设置 Sprite: sprite={img.sprite != null}, enabled={img.enabled}");
            }
            else
            {
                UploadDebug("ERROR: 找不到上传按钮里的 Image 组件");
            }

            if (btn != null)
            {
                string fileNameForButton = file;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    UploadDebug($"点击删除上传图片: {fileNameForButton}");
                    GameDataManager.RemoveUploadedImage(fileNameForButton);
                    PopulateUploadManagePanel();
                });
            }

            Text label = btnObj.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = "删除";

            createdCount++;
            UploadDebug($"上传图片 UI 创建完成: {createdCount}/{files.Count}");

            yield return null;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(uploadScrollContent);

        UploadDebug($"布局刷新完成: content.childCount={uploadScrollContent.childCount}, createdCount={createdCount}");

        uploadPanelCoroutine = null;
        UploadDebug("========== 上传 UI 刷新完成 ==========");
    }

    private void OnUploadButtonClicked()
    {
        ClearUploadDebug();
        UploadDebug("========== 点击上传图片 ==========");
        UploadDebug($"平台: {Application.platform}");
        UploadDebug($"persistentDataPath: {Application.persistentDataPath}");

#if UNITY_EDITOR
        UploadDebug("运行在 Unity Editor，打开文件选择器");
        string path = UnityEditor.EditorUtility.OpenFilePanel("选择图片", "", "png,jpg,jpeg");

        UploadDebug($"Editor 选择结果: {path}");

        if (!string.IsNullOrEmpty(path))
            StartCoroutine(ProcessUploadedImageAsync(path));
        else
            UploadDebug("用户取消选择");
#else
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
                Destroy(texture);
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
    /// Editor / 文件路径模式使用的后台解码流程。
    /// </summary>
    private IEnumerator ProcessUploadedImageAsync(string sourcePath)
    {
        UploadDebug("========== ProcessUploadedImageAsync ==========");

        if (string.IsNullOrEmpty(sourcePath))
        {
            UploadDebug("ERROR: sourcePath 为空");
            ShowConfirm("上传失败:\nsourcePath 为空", null);
            yield break;
        }

        bool isContentUri = sourcePath.StartsWith("content://");
        UploadDebug($"sourcePath = {sourcePath}");
        UploadDebug($"isContentUri = {isContentUri}");

        if (!isContentUri && !File.Exists(sourcePath))
        {
            UploadDebug("ERROR: source 文件不存在");
            ShowConfirm($"上传失败:\n文件不存在\n{sourcePath}", null);
            yield break;
        }

        byte[] pngBytes = null;
        bool decodingDone = false;
        string decodeError = null;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                pngBytes = isContentUri
                    ? AndroidImageDecoder.DecodeUriToPngBytes(sourcePath)
                    : AndroidImageDecoder.DecodeToPngBytes(sourcePath);
            }
            catch (Exception e)
            {
                decodeError = e.ToString();
                pngBytes = null;
            }
            finally
            {
                decodingDone = true;
            }
        });

        while (!decodingDone)
            yield return null;

        if (!string.IsNullOrEmpty(decodeError))
            UploadDebug($"ERROR: 后台解码异常: {decodeError}");

        if (pngBytes == null || pngBytes.Length < 100)
        {
            string reason = AndroidImageDecoder.LastError;
            if (string.IsNullOrEmpty(reason))
                reason = "解码返回空";

            UploadDebug($"ERROR: 解码失败: {reason}");
            ShowConfirm($"上传失败:\n{reason}", null);
            yield break;
        }

        UploadDebug($"后台解码成功: {pngBytes.Length} bytes");

        // 统一交给保存流程处理
        yield return SaveUploadedPng(pngBytes);
    }

    #endregion

    #region 局域网分享

    private void OnShareButtonClicked()
    {
        UploadDebug("========== 点击【分享】 ==========");
        LANShareManager.Instance.StartSharing();
        shareStatusText.text = "等待客户端连接...";
        StartCoroutine(WaitForClientConnection());
    }

    private IEnumerator WaitForClientConnection()
    {
        UploadDebug("等待客户端连接...");
        while (!LANShareManager.Instance.ClientConnected)
            yield return null;

        UploadDebug("客户端已连接，打开分享选择面板");
        shareStatusText.text = "连接成功";
        OpenShareSelectPanel();
    }

    private void OnReceiveButtonClicked()
    {
        UploadDebug("========== 点击【接收】 ==========");
        ShowPanel(deviceListPanel);
        StartDiscovery();
    }

    private void OpenShareSelectPanel()
    {
        shareSelectedFiles.Clear();
        ShowPanel(shareSelectPanel);
        PopulateShareSelectPanel();
    }

    /// <summary>
    /// 异步填充分享选择面板（防重入 + 释放旧动态 Sprite）。
    /// </summary>
    private void PopulateShareSelectPanel()
    {
        StartCoroutine(PopulateShareSelectPanelAsync());
    }

    private IEnumerator PopulateShareSelectPanelAsync()
    {
        if (isSharePanelLoading)
        {
            Debug.Log("分享面板正在加载中，忽略本次请求");
            yield break;
        }
        isSharePanelLoading = true;

        ReleaseDynamicSprites();

        foreach (Transform child in shareSelectContent)
            Destroy(child.gameObject);

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

            string fileName = file;
            Image capturedImg = img;
            Button capturedBtn = btn;
            btn.onClick.AddListener(() =>
            {
                if (shareSelectedFiles.Contains(fileName))
                {
                    shareSelectedFiles.Remove(fileName);
                    if (capturedImg != null) capturedImg.color = Color.white;
                }
                else
                {
                    shareSelectedFiles.Add(fileName);
                    if (capturedImg != null) capturedImg.color = Color.green;
                }
                UploadDebug($"[分享面板] 当前已选 {shareSelectedFiles.Count} 张");
            });

            yield return null;
        }

        isSharePanelLoading = false;
    }

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

        LANShareManager.Instance.SetSharedFiles(shareSelectedFiles);
        LANShareManager.Instance.NotifyClientsReady();

        shareStatusText.text = $"已分享 {shareSelectedFiles.Count} 张，等待下载...";
        UploadDebug("已发送就绪广播，分享面板保持打开（可继续修改选择）");

        // ★ 不关闭 shareSelectPanel，不切换面板
        // 用户可以继续勾选/取消，再次点击【分享】按钮会重新广播
    }

    private void StartDiscovery()
    {
        if (deviceListContent == null)
        {
            UploadDebug("ERROR: deviceListContent 未赋值！");
            return;
        }

        foreach (Transform child in deviceListContent)
            Destroy(child.gameObject);
        discoveredDevices.Clear();

        VerticalLayoutGroup layout = deviceListContent.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
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

        LANShareManager.Instance.StartDiscovery((deviceName, ip, port, isReady) =>
        {
            UploadDebug($"【发现回调】device={deviceName}, ip={ip}, port={port}, isReady={isReady}, connectedIP={connectedServerIP}");

            if (isReady)
            {
                UploadDebug($"【就绪广播】收到，IP={ip}");
                if (LANShareManager.Instance.ConnectedToServer)
                {
                    UploadDebug("【就绪广播】已连接状态，请求图片列表");
                    RequestRemoteImageList();
                }
                else
                {
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
                string entry = $"{deviceName}|{ip}|{port}";
                if (!discoveredDevices.Contains(entry))
                {
                    discoveredDevices.Add(entry);
                    AddDeviceButton(deviceName, ip, port, false);
                }
            }
        });
    }

    private void RequestRemoteImageList()
    {
        UploadDebug("开始请求远程图片列表...");

        LANShareManager.Instance.DownloadImageList((list) =>
        {
            UploadDebug($"收到远程列表：{list.Count} 张");
            remoteImageFiles = list;

            if (list.Count == 0)
            {
                remoteStatusText.text = "对方还没有分享图片，等待中...";
            }
            else
            {
                remoteStatusText.text = $"对方分享了 {list.Count} 张，点击【确认下载】";
            }
        });
    }

    private void AddDeviceButton(string deviceName, string ip, int port, bool isReady)
    {
        GameObject btnObj = Instantiate(deviceButtonPrefab, deviceListContent);
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(deviceButtonWidth, deviceButtonHeight);

        Button btn = btnObj.GetComponent<Button>();
        Text label = btnObj.GetComponentInChildren<Text>();
        if (label != null)
        {
            string status = isReady ? "（可下载）" : "";
            label.text = $"{deviceName} ({ip}){status}";
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

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

    private void PopulateRemoteImages()
    {
        // 新流程不使用缩略图列表，仅清空
        foreach (Transform child in remoteImageContent)
            Destroy(child.gameObject);
    }

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
            StartCoroutine(DownloadAllCoroutine(new List<string>(list)));
        });
    }
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

            LANShareManager.Instance.DownloadImageToShared(fn, (path) =>
            {
                savedPath = path;
                done = true;
            });

            // 等一帧，让 Unity 主线程有机会刷新 UI、也让 socket 有时间稳定
            yield return null;

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

            remoteStatusText.text = $"进度 {i + 1}/{total}（成功 {completed}，失败 {failed}）";

            // 文件之间给 100ms 缓冲
            yield return new WaitForSeconds(0.1f);
        }

        remoteStatusText.text = $"下载完成：成功 {completed}/{total}，失败 {failed}";
        UploadDebug($"========== 下载结束：成功 {completed}，失败 {failed} ==========");
    }
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

    private void HandleSharingStopped()
    {
        UploadDebug("【事件】OnSharingStopped 触发");
        if (shareStatusText != null) shareStatusText.text = "分享已停止";
    }

    #endregion

    #region 共享分类

    private void OnSharedCategoryClicked()
    {
        selectedCategory = GameDataManager.SharedCategory;
        ShowPanel(sharedImagePanel);
        PopulateSharedImagePanel();
    }

    /// <summary>
    /// 异步填充共享图片面板（防重入 + 释放旧动态 Sprite）。
    /// </summary>
    private void PopulateSharedImagePanel()
    {
        StartCoroutine(PopulateSharedImagePanelAsync());
    }

    private IEnumerator PopulateSharedImagePanelAsync()
    {
        if (isSharedPanelLoading)
        {
            Debug.Log("共享面板正在加载中，忽略本次请求");
            yield break;
        }
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
            btn.onClick.AddListener(() => OnSharedImageClicked(fileName));

            yield return null;
        }

        isSharedPanelLoading = false;
    }

    private void OnSharedImageClicked(string fileName)
    {
        selectedImageIndex = GameDataManager.GetSharedImages().IndexOf(fileName);
        ShowPanel(difficultyPanel);
    }

    #endregion

    #region 动态 Sprite 释放

    /// <summary>
    /// 释放所有动态创建的 Sprite 及其纹理（用于上传/共享图片）。
    /// 在切换面板或场景销毁时调用。
    /// </summary>
    private void ReleaseDynamicSprites()
    {
        if (dynamicSprites == null) return;

        foreach (var sprite in dynamicSprites)
        {
            if (sprite == null) continue;

            if (sprite.texture != null)
                Destroy(sprite.texture);

            Destroy(sprite);
        }

        dynamicSprites.Clear();
    }

    #endregion

    #region 广告与其他按钮

    private void OnAdStaminaClicked()
    {
        adStaminaButton.interactable = false;

        ShowRewardedAd(() =>
        {
            GameDataManager.AddStamina(4);
            GameDataManager.AddCoins(2);
            UpdateStaminaDisplay();
            UpdateCoinDisplay();
            Debug.Log("观看广告成功，获得4体力、2金币");
            adStaminaButton.interactable = true;
        }, () =>
        {
            Debug.Log("广告未完成，无奖励");
            adStaminaButton.interactable = true;
        });
    }

    private void ShowRewardedAd(Action onSuccess, Action onFail)
    {
        onSuccess?.Invoke();
    }

    private void OnDailyPuzzleClicked()
    {
        if (GameDataManager.IsDailyPuzzleCompletedToday())
        {
            ShowConfirm("今日每日拼图已完成，明天再来吧！", null);
            return;
        }

        if (!GameDataManager.IsDailyPuzzleGeneratedToday())
            GameDataManager.GenerateDailyPuzzle();

        PlayerPrefs.SetInt("IsDailyPuzzle", 1);
        PlayerPrefs.Save();
        SceneManager.LoadScene("GameScene");
    }

    #endregion

    #region 通用确认弹窗

    private void ShowConfirm(string message, Action onConfirm)
    {
        confirmText.text = message;
        confirmAction = onConfirm;
        confirmPanel.SetActive(true);
        confirmPanel.transform.SetAsLastSibling();

        Canvas confirmCanvas = confirmPanel.GetComponent<Canvas>();
        if (confirmCanvas == null)
        {
            confirmCanvas = confirmPanel.AddComponent<Canvas>();
            confirmPanel.AddComponent<GraphicRaycaster>();
        }
        confirmCanvas.overrideSorting = true;
        confirmCanvas.sortingOrder = 1000;
    }

    private void OnConfirmYes()
    {
        confirmPanel.SetActive(false);
        confirmAction?.Invoke();
    }

    #endregion

    #region 游戏启动

    private void StartGame(int gridSize)
    {
        PlayerPrefs.SetString("SelectedCategory", selectedCategory);
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.SetInt("SelectedImageIndex", selectedImageIndex);
        PlayerPrefs.Save();

        if (loadingSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlayLoadingSound(loadingSound);

        loadingPanel.SetActive(true);
        loadingPanel.transform.SetAsLastSibling();
        if (loadingText != null) loadingText.text = "加载中...";

        StartCoroutine(LoadGameAsync());
    }

    private IEnumerator LoadGameAsync()
    {
        float startTime = Time.realtimeSinceStartup;
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("GameScene");
        asyncLoad.allowSceneActivation = false;

        float displayProgress = 0f;
        float realProgress = 0f;

        while (displayProgress < 1f || asyncLoad.progress < 0.9f)
        {
            realProgress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            float timeProgress = Mathf.Clamp01((Time.realtimeSinceStartup - startTime) / 3f);
            displayProgress = Mathf.Min(realProgress, timeProgress);

            if (loadingText != null)
                loadingText.text = $"加载中... {Mathf.RoundToInt(displayProgress * 100)}%";
            if (loadingSlider != null)
                loadingSlider.value = displayProgress;

            yield return null;
        }

        if (loadingText != null) loadingText.text = "加载中... 100%";

        if (SoundManager.Instance != null)
            SoundManager.Instance.StopLoadingSound();

        asyncLoad.allowSceneActivation = true;
    }

    #endregion

    #region 个人信息

    private void UpdateCoinDisplay()
    {
        if (coinText != null) coinText.text = "金币：" + GameDataManager.Coins;
    }

    private void OnChangeNameClicked()
    {
        panelAfterNameChange = profilePanel;
        nameInputField.text = GameDataManager.PlayerName;
        ShowPanel(nameInputPanel);
    }

    private void OnNameConfirmed()
    {
        string name = nameInputField.text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowConfirm("名字不能为空", null);
            return;
        }
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9]+$"))
        {
            ShowConfirm("名字只能包含字母和数字", null);
            nameInputField.text = "";
            return;
        }

        confirmPanel.SetActive(false);
        GameDataManager.PlayerName = name;
        ShowPanel(panelAfterNameChange);
    }

    private void UpdateProfileUI()
    {
        if (profileAvatarImage != null)
        {
            if (avatarSprites == null || avatarSprites.Length == 0)
                avatarSprites = Resources.LoadAll<Sprite>("Art/HeadPicture");

            int avatarIndex = GameDataManager.GetAvatarIndex();
            if (avatarSprites != null && avatarSprites.Length > 0)
            {
                if (avatarIndex < 0 || avatarIndex >= avatarSprites.Length)
                    avatarIndex = 0;
                profileAvatarImage.sprite = avatarSprites[avatarIndex];
            }
            else
            {
                Debug.LogWarning("没有找到头像图片");
            }
        }

        if (profileNameText != null) profileNameText.text = GameDataManager.PlayerName;
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

    private void OpenAvatarSelectPanel()
    {
        foreach (Transform child in avatarScrollContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = avatarScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = avatarScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(150, 150);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        avatarSprites = Resources.LoadAll<Sprite>("Art/HeadPicture");
        if (avatarSprites.Length == 0)
        {
            Debug.LogWarning("No avatars found in Art/HeadPicture");
            return;
        }

        Array.Sort(avatarSprites, (a, b) => string.Compare(a.name, b.name));

        for (int i = 0; i < avatarSprites.Length; i++)
        {
            GameObject btnObj = Instantiate(avatarButtonPrefab, avatarScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = avatarSprites[i];

            int index = i;
            btn.onClick.AddListener(() => OnAvatarClicked(index));
        }

        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = avatarSelectPanel.GetComponentInChildren<ScrollRect>();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;

        ShowPanel(avatarSelectPanel);
    }

    private void OnAvatarClicked(int index)
    {
        GameDataManager.SetAvatarIndex(index);
        UpdateProfileUI();
        ShowPanel(profilePanel);
    }

    #endregion

    #region 收藏面板

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
            string[] parts = fav.Split('_');
            if (parts.Length != 2) continue;

            string category = parts[0];
            int imageIndex;
            if (!int.TryParse(parts[1], out imageIndex)) continue;

            Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites(category);
            Array.Sort(sprites, (a, b) => string.Compare(a.name, b.name));
            if (imageIndex < 0 || imageIndex >= sprites.Length) continue;

            GameObject btnObj = Instantiate(imageButtonPrefab, favoritesScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            Text label = btnObj.GetComponentInChildren<Text>();

            bool unlocked = GameDataManager.IsImageUnlocked(category, imageIndex);
            if (img != null)
            {
                img.sprite = sprites[imageIndex];
                img.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.7f);
            }
            if (label != null)
                label.text = unlocked ? "" : $"{GameDataManager.GetImagePrice(category, imageIndex)}金币";

            string cat = category;
            int idx = imageIndex;
            btn.onClick.AddListener(() =>
            {
                if (GameDataManager.IsImageUnlocked(cat, idx))
                {
                    selectedCategory = cat;
                    selectedImageIndex = idx;
                    ShowPanel(difficultyPanel);
                }
                else
                {
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