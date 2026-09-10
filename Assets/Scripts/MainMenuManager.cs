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
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    #region UI 引用 - 设置面板

    [Header("设置面板")]
    public GameObject settingsPanel;            // 设置面板
    public Button settingsButton;               // 个人面板中的设置按钮
    public Button settingsCloseButton;          // 设置面板关闭按钮
    public Button stopBGMButton;                // 停止/播放背景音乐按钮
    public Slider bgmSlider;                    // 背景音乐音量滑块
    public Slider sfxSlider;                    // 音效音量滑块

    [Header("退出游戏")]
    public Button quitGameButton;               // 退出游戏按钮

    [Header("加载音效")]
    public AudioClip loadingSound;              // 加载游戏时播放的音效

    #endregion

    #region UI 引用 - 局域网分享

    [Header("停止广播")]
    public Button stopBroadcastButton;          // 停止分享按钮

    [Header("局域网分享 UI")]
    public GameObject deviceListPanel;          // 设备列表面板
    public RectTransform deviceListContent;     // 设备按钮父物体
    public GameObject deviceButtonPrefab;       // 设备按钮预制体
    public float deviceButtonWidth = 620f;
    public float deviceButtonHeight = 100f;
    public Button cancelDiscoverButton;         // 取消发现按钮

    public GameObject remoteImagePanel;         // 远程图片面板（多选下载）
    public RectTransform remoteImageContent;    // 远程图片按钮父物体
    public GameObject remoteImageButtonPrefab;  // 远程图片按钮预制体
    public Button cancelConnectButton;          // 取消连接按钮
    public Button downloadSelectedButton;       // 下载选中按钮
    public Text remoteStatusText;               // 远程面板状态文字

    public GameObject shareSelectPanel;         // 分享选择面板
    public RectTransform shareSelectContent;    // 分享选择图片父物体
    public Button startSharingButton;           // 开始分享按钮
    public Button cancelShareSelectButton;      // 取消分享选择按钮

    [Header("局域网分享")]
    public Button shareButton;                  // 分享入口按钮
    public Button receiveButton;                // 接收入口按钮
    public Text shareStatusText;                // 分享状态文本

    [Header("共享分类")]
    public GameObject sharedImagePanel;         // 共享图片面板
    public RectTransform sharedImageContent;    // 共享图片父物体
    public Button sharedCloseButton;            // 关闭共享面板按钮

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

    private bool isFlashing = false;                    // 金币文本是否正在闪烁
    private string selectedCategory;                    // 当前选中的分类
    private int selectedImageIndex = -1;                // 当前选中的图片索引（-1 为随机）
    private string pendingPurchaseCategory;             // 待购买图片所属分类
    private int pendingPurchaseImageIndex;              // 待购买图片索引

    // 面板层级管理
    private GameObject currentBasePanel;                // 基础面板（categoryScrollView 或 profilePanel）
    private GameObject currentLayer2Panel;              // 第二层面板
    private GameObject currentLayer3Panel;              // 第三层面板
    private GameObject currentLayer4Panel;              // 第四层面板

    // 预览图协程缓存，用于清理
    private Dictionary<Button, Coroutine> previewCoroutines = new Dictionary<Button, Coroutine>();

    private Action confirmAction;                       // 确认弹窗回调
    private GameObject panelAfterNameChange;            // 名字修改成功后返回的面板
    private Sprite[] avatarSprites;                     // 头像缓存

    // 局域网分享状态
    private List<string> discoveredDevices = new List<string>();    // "设备名|IP|端口"
    private List<string> remoteImageFiles = new List<string>();     // 远程图片文件名列表
    private HashSet<int> selectedRemoteIndices = new HashSet<int>();// 选中的远程图片索引
    private List<string> shareSelectedFiles = new List<string>();   // 待分享的文件
    private string connectedServerIP = null;            // 当前已连接的服务端 IP

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        // GameDataManager.ResetForEditor(); // 测试用
    }

    private void Start()
    {
        // 订阅分享停止事件
        LANShareManager.Instance.OnSharingStopped += HandleSharingStopped;

        // 等待 AssetBundle 加载完成后再生成分类按钮
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
            LANShareManager.Instance.StopDiscovery();
            ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        cancelConnectButton.onClick.AddListener(() =>
        {
            LANShareManager.Instance.DisconnectFromServer();
            ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        downloadSelectedButton.onClick.AddListener(DownloadSelectedImages);
        startSharingButton.onClick.AddListener(StartSharingSelectedFiles);
        cancelShareSelectButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? profilePanel));

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
            if (currentLayer2Panel != null)
                ShowPanel(currentLayer2Panel);
            else
                ShowPanel(currentBasePanel ?? categoryScrollView);
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
        if (LANShareManager.Instance != null)
            LANShareManager.Instance.OnSharingStopped -= HandleSharingStopped;
    }

    #endregion

    #region AssetBundle 等待

    /// <summary>
    /// 等待 AssetBundle 加载完成后再生成分类按钮。
    /// </summary>
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

    /// <summary>
    /// 根据目标面板类型显示对应面板，并管理层级关系。
    /// </summary>
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

            // 隐藏所有第二层面板
            imageSelectPanel.SetActive(false);
            favoritesPanel.SetActive(false);
            uploadManagePanel.SetActive(false);
            avatarSelectPanel.SetActive(false);
            shareSelectPanel.SetActive(false);
            deviceListPanel.SetActive(false);
            remoteImagePanel.SetActive(false);
            sharedImagePanel.SetActive(false);
            settingsPanel.SetActive(false);

            // 显示目标面板
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

        // 面板内容刷新
        if (panelToShow == profilePanel) UpdateProfileUI();
        if (panelToShow == favoritesPanel) PopulateFavoritesPanel();
        if (panelToShow == uploadManagePanel) PopulateUploadManagePanel();
    }

    /// <summary>隐藏所有覆盖面板。</summary>
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

    /// <summary>隐藏第二层以上的所有覆盖面板。</summary>
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

    /// <summary>隐藏第三层以上的所有覆盖面板。</summary>
    private void HidePanelsAboveLayer3()
    {
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentLayer4Panel = null;
    }

    /// <summary>隐藏第四层以上的所有覆盖面板。</summary>
    private void HidePanelsAboveLayer4()
    {
        nameInputPanel.SetActive(false);
    }

    /// <summary>隐藏所有面板（包括基础面板）。</summary>
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

    /// <summary>打开设置面板，并同步滑块值。</summary>
    private void OpenSettingsPanel()
    {
        if (SoundManager.Instance != null)
        {
            bgmSlider.value = SoundManager.Instance.BGMVolume;
            sfxSlider.value = SoundManager.Instance.SFXVolume;
        }
        ShowPanel(settingsPanel);
    }

    /// <summary>退出游戏（编辑器下停止 Play 模式）。</summary>
    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>切换背景音乐的播放/停止。</summary>
    private void ToggleBGM()
    {
        if (SoundManager.Instance == null) return;

        if (SoundManager.Instance.IsBGMPlaying)
            SoundManager.Instance.StopBGM();
        else
            SoundManager.Instance.PlayBGM();

        UpdateBGMButtonText();
    }

    /// <summary>根据当前 BGM 状态更新按钮文字。</summary>
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

    /// <summary>
    /// 动态生成所有分类按钮（含上传和共享特殊分类）。
    /// </summary>
    private void GenerateCategoryButtons()
    {
        // 停止旧的预览协程
        foreach (var kvp in previewCoroutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        previewCoroutines.Clear();

        // 清空旧按钮
        foreach (Transform child in categoryScrollContent)
            Destroy(child.gameObject);

        // 设置 GridLayoutGroup
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

    /// <summary>为指定分类的预览图循环切换显示。</summary>
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

    /// <summary>图片渐变切换（淡出-换图-淡入）。</summary>
    private IEnumerator FadeToSprite(Image image, Sprite newSprite)
    {
        if (image == null) yield break;

        float elapsed = 0f;
        Color startColor = image.color;
        Color transparentColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        // 淡出
        while (elapsed < fadeDuration)
        {
            if (image == null) yield break;
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(startColor, transparentColor, elapsed / fadeDuration);
            yield return null;
        }

        if (image == null) yield break;
        image.sprite = newSprite;

        // 淡入
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

    /// <summary>分类按钮点击：打开图片选择面板。</summary>
    private void OnCategoryClicked(int index)
    {
        // 上传分类
        if (index == GameDataManager.Categories.Length)
        {
            selectedCategory = GameDataManager.UploadCategory;
            OpenImageSelectPanel(GameDataManager.UploadCategory);
            ShowPanel(imageSelectPanel);
            return;
        }

        // 普通分类
        string category = GameDataManager.Categories[index];
        selectedCategory = category;
        OpenImageSelectPanel(category);
        ShowPanel(imageSelectPanel);
    }

    /// <summary>填充图片选择面板（上传或普通分类）。</summary>
    private void OpenImageSelectPanel(string category)
    {
        foreach (Transform child in imageScrollContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = imageScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = imageScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(300, 300);
        grid.spacing = new Vector2(40, 40);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        List<Sprite> sprites = new List<Sprite>();
        List<string> imageNames = new List<string>();

        // 上传分类：从文件系统加载
        if (category == GameDataManager.UploadCategory)
        {
            List<string> files = GameDataManager.GetUploadedImages();
            foreach (string file in files)
            {
                string path = GameDataManager.GetUploadedImagePath(file);
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(2, 2);
                    tex.LoadImage(bytes);
                    sprites.Add(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)));
                    imageNames.Add(file);
                }
            }
        }
        // 普通分类：从 AssetBundle 加载
        else
        {
            Sprite[] loadedSprites = AssetBundleManager.Instance.GetCategorySprites(category);
            Array.Sort(loadedSprites, (a, b) => string.Compare(a.name, b.name));
            sprites.AddRange(loadedSprites);
            for (int i = 0; i < sprites.Count; i++) imageNames.Add(sprites[i].name);
        }

        // 创建图片按钮
        for (int i = 0; i < sprites.Count; i++)
        {
            GameObject imgBtnObj = Instantiate(imageButtonPrefab, imageScrollContent);
            Button imgBtn = imgBtnObj.GetComponent<Button>();
            Image img = imgBtnObj.transform.Find("Image")?.GetComponent<Image>();
            Text label = imgBtnObj.GetComponentInChildren<Text>();

            if (img != null) img.sprite = sprites[i];

            if (category == GameDataManager.UploadCategory)
            {
                if (label != null) label.text = "";
            }
            else
            {
                bool unlocked = GameDataManager.IsImageUnlocked(category, i);
                if (img != null)
                    img.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.7f);
                if (label != null)
                    label.text = unlocked ? "" : $"{GameDataManager.GetImagePrice(category, i)}金币";
            }

            int imageIndex = i;
            imgBtn.onClick.AddListener(() => OnImageClicked(imageIndex));
        }

        imageSelectPanel.SetActive(true);
    }

    /// <summary>图片点击：解锁则进入难度选择，未解锁则弹出购买。</summary>
    private void OnImageClicked(int imageIndex)
    {
        // 上传分类：直接进入难度选择
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            selectedImageIndex = imageIndex;
            ShowPanel(difficultyPanel);
            return;
        }

        // 随机
        if (imageIndex == -1)
        {
            selectedImageIndex = -1;
            ShowPanel(difficultyPanel);
        }
        // 普通分类
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

    /// <summary>确认购买（分类或图片）。</summary>
    private void ConfirmPurchase()
    {
        // 购买分类
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
        // 购买图片
        else
        {
            int price = GameDataManager.GetImagePrice(pendingPurchaseCategory, pendingPurchaseImageIndex);
            if (GameDataManager.SpendCoins(price))
            {
                GameDataManager.UnlockImage(pendingPurchaseCategory, pendingPurchaseImageIndex);
                UpdateCoinDisplay();
                OpenImageSelectPanel(pendingPurchaseCategory);
                ShowPanel(imageSelectPanel);
                Debug.Log($"解锁图片 {pendingPurchaseCategory}_{pendingPurchaseImageIndex} 成功！");
            }
            else
            {
                purchaseText.text = "金币不足！";
                StartCoroutine(FlashCoinTextRed());
            }
        }
    }

    /// <summary>金币不足时文本闪烁红色。</summary>
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

    /// <summary>打开上传管理面板。</summary>
    private void OpenUploadManagePanel()
    {
        PopulateUploadManagePanel();
        ShowPanel(uploadManagePanel);
    }

    /// <summary>填充上传管理面板（缩略图 + 删除按钮）。</summary>
    private void PopulateUploadManagePanel()
    {
        foreach (Transform child in uploadScrollContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = uploadScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = uploadScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 200);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter fitter = uploadScrollContent.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = uploadScrollContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        List<string> files = GameDataManager.GetUploadedImages();
        foreach (string file in files)
        {
            string path = GameDataManager.GetUploadedImagePath(file);
            if (!File.Exists(path)) continue;

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            GameObject btnObj = Instantiate(imageButtonPrefab, uploadScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = sprite;

            string fileName = file;
            btn.onClick.AddListener(() =>
            {
                GameDataManager.RemoveUploadedImage(fileName);
                PopulateUploadManagePanel();
            });

            Text label = btnObj.GetComponentInChildren<Text>();
            if (label != null) label.text = "删除";
        }
    }

    /// <summary>上传按钮点击：从相册选择图片。</summary>
    private void OnUploadButtonClicked()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("选择图片", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path))
            ProcessUploadedImage(path);
#else
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (path != null)
                ProcessUploadedImage(path);
            else
                Debug.Log("用户取消选择或出错");
        }, "选择图片", "image/*");
#endif
    }

    /// <summary>将选择的图片复制到持久化目录并记录到上传列表。</summary>
    private void ProcessUploadedImage(string sourcePath)
    {
        try
        {
            string uploadDir = Path.Combine(Application.persistentDataPath, "Uploads");
            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

            string fileName = "upload_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + Path.GetExtension(sourcePath);
            string destPath = Path.Combine(uploadDir, fileName);
            File.Copy(sourcePath, destPath, true);

            GameDataManager.AddUploadedImage(fileName);
            Debug.Log("上传成功: " + fileName);

            if (categoryScrollView.activeSelf) GenerateCategoryButtons();
            PopulateUploadManagePanel();
        }
        catch (Exception e)
        {
            Debug.LogError("上传图片失败: " + e.Message);
        }
    }

    #endregion

    #region 局域网分享

    /// <summary>分享按钮点击：启动分享，等待客户端连接。</summary>
    private void OnShareButtonClicked()
    {
        LANShareManager.Instance.StartSharing();
        shareStatusText.text = "等待客户端连接...";
        StartCoroutine(WaitForClientConnection());
    }

    /// <summary>等待客户端连接成功后弹出分享选择面板。</summary>
    private IEnumerator WaitForClientConnection()
    {
        while (!LANShareManager.Instance.ClientConnected)
            yield return null;

        shareStatusText.text = "连接成功";
        OpenShareSelectPanel();
    }

    /// <summary>接收按钮点击：打开设备列表，开始发现设备。</summary>
    private void OnReceiveButtonClicked()
    {
        ShowPanel(deviceListPanel);
        StartDiscovery();
    }

    /// <summary>打开分享选择面板。</summary>
    private void OpenShareSelectPanel()
    {
        shareSelectedFiles.Clear();
        PopulateShareSelectPanel();
        ShowPanel(shareSelectPanel);
    }

    /// <summary>填充分享选择面板（显示所有上传图片，可多选）。</summary>
    private void PopulateShareSelectPanel()
    {
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
        foreach (string file in files)
        {
            string path = GameDataManager.GetUploadedImagePath(file);
            if (!File.Exists(path)) continue;

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            GameObject btnObj = Instantiate(imageButtonPrefab, shareSelectContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = sprite;

            string fileName = file;
            btn.onClick.AddListener(() =>
            {
                if (shareSelectedFiles.Contains(fileName))
                {
                    shareSelectedFiles.Remove(fileName);
                    img.color = Color.white;
                }
                else
                {
                    shareSelectedFiles.Add(fileName);
                    img.color = Color.green;
                }
            });
        }
    }

    /// <summary>开始分享选中的文件，发送就绪广播。</summary>
    private void StartSharingSelectedFiles()
    {
        if (shareSelectedFiles.Count == 0)
        {
            ShowConfirm("请至少选择一张图片", null);
            return;
        }

        LANShareManager.Instance.SetSharedFiles(shareSelectedFiles);
        LANShareManager.Instance.NotifyClientsReady();
        shareSelectPanel.SetActive(false);
        shareStatusText.text = "已发送，等待客户端下载...";

        ShowPanel(currentBasePanel ?? profilePanel);
    }

    /// <summary>开始发现局域网设备。</summary>
    private void StartDiscovery()
    {
        if (deviceListContent == null)
        {
            Debug.LogError("deviceListContent 未赋值！");
            return;
        }

        foreach (Transform child in deviceListContent)
            Destroy(child.gameObject);
        discoveredDevices.Clear();

        // 使用 VerticalLayoutGroup 排列设备按钮
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
            if (isReady)
            {
                // 收到就绪广播，且 IP 匹配已连接的服务器 → 请求图片列表
                if (connectedServerIP == ip)
                {
                    Debug.Log("收到就绪广播且IP匹配，请求图片列表");
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
                }
            }
            else
            {
                // 普通广播：添加到设备列表
                string entry = $"{deviceName}|{ip}|{port}";
                if (!discoveredDevices.Contains(entry))
                {
                    discoveredDevices.Add(entry);
                    AddDeviceButton(deviceName, ip, port, false);
                }
            }
        });
    }

    /// <summary>请求远程图片列表并显示在远程图片面板。</summary>
    private void RequestRemoteImageList()
    {
        LANShareManager.Instance.DownloadImageList((list) =>
        {
            remoteImageFiles = list;
            selectedRemoteIndices.Clear();
            PopulateRemoteImages();
            remoteStatusText.text = $"共 {list.Count} 张图片，可多选下载";
        });
    }

    /// <summary>添加设备按钮到设备列表。</summary>
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
            // 连接服务器，但不停止发现（以便接收就绪广播）
            LANShareManager.Instance.ConnectToServer(ip, port);
            if (!LANShareManager.Instance.ConnectedToServer)
            {
                remoteStatusText.text = "连接失败";
                return;
            }

            connectedServerIP = ip;
            deviceListPanel.SetActive(false);
            remoteImagePanel.SetActive(true);
            remoteStatusText.text = "已连接，等待对方分享...";
        });
    }

    /// <summary>填充远程图片面板（多选下载）。</summary>
    private void PopulateRemoteImages()
    {
        foreach (Transform child in remoteImageContent)
            Destroy(child.gameObject);

        // 设置 Content 的 RectTransform
        remoteImageContent.anchorMin = new Vector2(0, 1);
        remoteImageContent.anchorMax = new Vector2(1, 1);
        remoteImageContent.pivot = new Vector2(0.5f, 1);
        remoteImageContent.anchoredPosition = Vector2.zero;
        remoteImageContent.sizeDelta = new Vector2(0, 0);

        // 设置 GridLayoutGroup
        GridLayoutGroup grid = remoteImageContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = remoteImageContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 200);
        grid.spacing = new Vector2(20, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 设置 ContentSizeFitter
        ContentSizeFitter fitter = remoteImageContent.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = remoteImageContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // 生成按钮
        for (int i = 0; i < remoteImageFiles.Count; i++)
        {
            string fileName = remoteImageFiles[i];
            GameObject btnObj = Instantiate(remoteImageButtonPrefab, remoteImageContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            Text label = btnObj.GetComponentInChildren<Text>();
            if (label != null) label.text = fileName;
            if (img != null) img.color = Color.white;

            int index = i;
            btn.onClick.AddListener(() => ToggleRemoteSelection(index, btnObj));
        }

        // 强制刷新布局
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(remoteImageContent);
    }

    /// <summary>切换远程图片的选中状态。</summary>
    private void ToggleRemoteSelection(int index, GameObject btnObj)
    {
        if (selectedRemoteIndices.Contains(index))
        {
            selectedRemoteIndices.Remove(index);
            btnObj.GetComponent<Image>().color = Color.white;
        }
        else
        {
            selectedRemoteIndices.Add(index);
            btnObj.GetComponent<Image>().color = Color.green;
        }
    }

    /// <summary>下载选中的远程图片到共享分类。</summary>
    private void DownloadSelectedImages()
    {
        if (selectedRemoteIndices.Count == 0)
        {
            remoteStatusText.text = "请先选择图片";
            return;
        }

        remoteStatusText.text = "正在下载...";
        int total = selectedRemoteIndices.Count;
        int completed = 0;

        foreach (int index in selectedRemoteIndices)
        {
            string fileName = remoteImageFiles[index];
            LANShareManager.Instance.DownloadImageToShared(fileName, (path) =>
            {
                completed++;
                remoteStatusText.text = $"正在下载 {completed}/{total} 张...";

                if (completed >= total)
                {
                    remoteStatusText.text = "下载完成，图片已进入共享分类";
                    if (sharedImagePanel.activeSelf) PopulateSharedImagePanel();
                }
            });
        }
    }

    /// <summary>分享停止事件回调。</summary>
    private void HandleSharingStopped()
    {
        if (shareStatusText != null) shareStatusText.text = "分享已停止";
    }

    #endregion

    #region 共享分类

    /// <summary>共享分类按钮点击：打开共享图片面板。</summary>
    private void OnSharedCategoryClicked()
    {
        selectedCategory = GameDataManager.SharedCategory;
        PopulateSharedImagePanel();
        ShowPanel(sharedImagePanel);
    }

    /// <summary>填充共享图片面板。</summary>
    private void PopulateSharedImagePanel()
    {
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
        foreach (string file in files)
        {
            string path = GameDataManager.GetSharedImagePath(file);
            if (!File.Exists(path)) continue;

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            GameObject btnObj = Instantiate(imageButtonPrefab, sharedImageContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = sprite;

            string fileName = file;
            btn.onClick.AddListener(() => OnSharedImageClicked(fileName));
        }
    }

    /// <summary>点击共享图片：进入难度选择。</summary>
    private void OnSharedImageClicked(string fileName)
    {
        selectedImageIndex = GameDataManager.GetSharedImages().IndexOf(fileName);
        ShowPanel(difficultyPanel);
    }

    #endregion

    #region 广告与其他按钮

    /// <summary>看广告恢复体力按钮。</summary>
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

    /// <summary>模拟广告：实际项目替换为真实广告 SDK。</summary>
    private void ShowRewardedAd(Action onSuccess, Action onFail)
    {
        onSuccess?.Invoke();
    }

    /// <summary>每日拼图按钮。</summary>
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

    /// <summary>显示确认弹窗。</summary>
    private void ShowConfirm(string message, Action onConfirm)
    {
        confirmText.text = message;
        confirmAction = onConfirm;
        confirmPanel.SetActive(true);
        confirmPanel.transform.SetAsLastSibling();

        // 确保弹窗在最高层
        Canvas confirmCanvas = confirmPanel.GetComponent<Canvas>();
        if (confirmCanvas == null)
        {
            confirmCanvas = confirmPanel.AddComponent<Canvas>();
            confirmPanel.AddComponent<GraphicRaycaster>();
        }
        confirmCanvas.overrideSorting = true;
        confirmCanvas.sortingOrder = 1000;
    }

    /// <summary>确认弹窗点击“是”。</summary>
    private void OnConfirmYes()
    {
        confirmPanel.SetActive(false);
        confirmAction?.Invoke();
    }

    #endregion

    #region 游戏启动

    /// <summary>启动游戏：保存参数、播放加载音效、异步加载场景。</summary>
    private void StartGame(int gridSize)
    {
        PlayerPrefs.SetString("SelectedCategory", selectedCategory);
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.SetInt("SelectedImageIndex", selectedImageIndex);
        PlayerPrefs.Save();

        // 播放加载音效
        if (loadingSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlayLoadingSound(loadingSound);

        loadingPanel.SetActive(true);
        loadingPanel.transform.SetAsLastSibling();
        if (loadingText != null) loadingText.text = "加载中...";

        StartCoroutine(LoadGameAsync());
    }

    /// <summary>异步加载游戏场景，至少显示 3 秒加载画面。</summary>
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
        // 头像
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

        // 名字、等级、经验
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