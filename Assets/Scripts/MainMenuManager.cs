using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Text.RegularExpressions;

/// <summary>
/// 主菜单管理器：负责主菜单所有 UI 面板的切换、分类按钮生成、图片选择、
/// 个人信息、收藏、每日拼图、看广告恢复体力、购买等功能。
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // ==================== UI 引用 ====================

    [Header("每日拼图")]
    public Button dailyPuzzleButton;                // 每日拼图入口按钮

    [Header("加载面板")]
    public GameObject loadingPanel;   // 加载面板物体
    public Text loadingText;          // 加载提示文字

    [Header("加载进度条")]
    public Slider loadingSlider;

    [Header("通用确认弹窗")]
    public GameObject confirmPanel;                 // 确认弹窗面板
    public Text confirmText;                        // 弹窗提示文字
    public Button confirmYesButton;                 // 确定按钮
    public Button confirmNoButton;                  // 取消按钮
    private System.Action confirmAction;            // 确定按钮的回调

    [Header("广告恢复")]
    public Button adStaminaButton;                  // 看广告恢复体力按钮

    [Header("体力显示")]
    public Text staminaText;                        // 体力数值文本
    public Slider staminaSlider;                    // 体力条

    [Header("分类 ScrollView")]
    public GameObject categoryScrollView;           // 分类滚动视图（整个物体）
    public RectTransform categoryScrollContent;     // 分类按钮的父物体
    public GameObject categoryButtonPrefab;         // 分类按钮预制体

    [Header("分类预览设置")]
    public float previewChangeInterval = 3f;        // 预览图切换间隔（秒）
    public float fadeDuration = 0.5f;               // 渐变时长

    [Header("图片选择面板")]
    public GameObject imageSelectPanel;             // 图片选择面板
    public Button closeImagePanelButton;            // 关闭图片面板按钮
    public RectTransform imageScrollContent;        // 图片按钮的父物体
    public GameObject imageButtonPrefab;            // 图片按钮预制体

    [Header("难度面板")]
    public GameObject difficultyPanel;              // 难度选择面板
    public Button easyButton;                       // 简单难度按钮
    public Button normalButton;                     // 普通难度按钮
    public Button hardButton;                       // 困难难度按钮

    [Header("购买面板")]
    public GameObject purchasePanel;                // 购买面板
    public Text purchaseText;                       // 购买提示文字
    public Button confirmPurchaseButton;            // 确认购买按钮
    public Button cancelPurchaseButton;             // 取消购买按钮

    [Header("金币显示")]
    public Text coinText;                           // 金币数量文本

    [Header("个人信息")]
    public GameObject profilePanel;                 // 个人信息面板
    public Text profileNameText;                    // 玩家名字
    public Text profileLevelText;                   // 等级和经验文本
    public Slider experienceSlider;                 // 经验条
    public Button favoritesButton;                  // 我的收藏按钮

    [Header("姓名输入面板")]
    public GameObject nameInputPanel;               // 姓名输入面板
    public InputField nameInputField;               // 姓名输入框
    public Button nameConfirmButton;                // 确认名字按钮

    [Header("我的收藏面板")]
    public GameObject favoritesPanel;               // 收藏面板
    public RectTransform favoritesScrollContent;    // 收藏图片的父物体
    public Button favoritesCloseButton;             // 关闭收藏面板按钮

    [Header("底部按钮")]
    public Button profileButton;                    // 个人信息底部按钮
    public Button categoryButton;                   // 分类底部按钮

    [Header("难度面板取消按钮")]
    public Button difficultyCancelButton;           // 难度面板取消按钮

    // ==================== 私有状态 ====================

    private bool isFlashing = false;                // 金币文本是否正在闪烁
    private string selectedCategory;                // 当前选中的分类
    private int selectedImageIndex = -1;            // 当前选中的图片索引（-1为随机）
    private string pendingPurchaseCategory;         // 待购买的分类
    private int pendingPurchaseImageIndex;          // 待购买的图片索引

    // 面板层级管理
    private GameObject currentBasePanel;            // 当前基础面板（categoryScrollView 或 profilePanel）
    private GameObject currentLayer2Panel;          // 当前第二层面板（imageSelectPanel 或 favoritesPanel）
    private GameObject currentLayer3Panel;          // 当前第三层面板（difficultyPanel）
    private GameObject currentLayer4Panel;          // 当前第四层面板（purchasePanel）

    // 保存每个分类按钮上的预览协程，用于停止和清理
    private Dictionary<Button, Coroutine> previewCoroutines = new Dictionary<Button, Coroutine>();

    void Awake()
    {
        // 测试时可启用此行清空数据，正式版请注释掉
        // GameDataManager.ResetForEditor();
    }

    void Start()
    {
        // 绑定每日拼图按钮
        dailyPuzzleButton.onClick.AddListener(OnDailyPuzzleClicked);

        //加载页面初始隐藏
        loadingPanel.SetActive(false);

        // 绑定确认弹窗按钮
        confirmYesButton.onClick.AddListener(OnConfirmYes);
        confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);

        // 绑定底部基础面板切换按钮
        profileButton.onClick.AddListener(() => ShowPanel(profilePanel));
        categoryButton.onClick.AddListener(() => ShowPanel(categoryScrollView));

        // 收藏面板相关
        favoritesButton.onClick.AddListener(() => ShowPanel(favoritesPanel));
        favoritesCloseButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? profilePanel));

        // 姓名确认按钮
        nameConfirmButton.onClick.AddListener(OnNameConfirmed);

        // 难度面板取消按钮
        difficultyCancelButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null)
                ShowPanel(currentLayer2Panel);
            else
                ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        // 初始化体力系统（第一次运行设置满体力）
        GameDataManager.InitStaminaSystem();

        // 启动体力 UI 更新协程
        StartCoroutine(UpdateStaminaUI());

        // 根据是否已设置名字决定初始面板
        if (string.IsNullOrEmpty(GameDataManager.PlayerName))
            ShowPanel(nameInputPanel);
        else
            ShowPanel(categoryScrollView);

        // 难度按钮绑定（测试时简单难度用 2x2，正式改为 6）
        easyButton.onClick.AddListener(() => StartGame(2));
        normalButton.onClick.AddListener(() => StartGame(8));
        hardButton.onClick.AddListener(() => StartGame(10));

        // 看广告恢复体力按钮
        adStaminaButton.onClick.AddListener(OnAdStaminaClicked);

        // 购买面板按钮
        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
        cancelPurchaseButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null) ShowPanel(currentLayer2Panel);
            else ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        // 关闭图片选择面板按钮
        closeImagePanelButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? categoryScrollView));

        // 更新金币显示并生成分类按钮
        UpdateCoinDisplay();
        GenerateCategoryButtons();
    }

    void OnEnable()
    {
        // 每次激活时刷新金币和个人信息
        UpdateCoinDisplay();
        UpdateProfileUI();
    }

    /// <summary>
    /// 根据目标面板类型显示对应面板，并管理面板之间的层级关系
    /// </summary>
    void ShowPanel(GameObject panelToShow)
    {
        // 如果目标为空，只隐藏所有覆盖面板
        if (panelToShow == null)
        {
            HideOverlayPanels();
            confirmPanel.SetActive(false);
            return;
        }

        // 无论显示哪个面板，先隐藏确认弹窗
        confirmPanel.SetActive(false);

        // 判断面板类型
        if (panelToShow == categoryScrollView || panelToShow == profilePanel)
        {
            // 基础面板互斥
            HideOverlayPanels();
            categoryScrollView.SetActive(panelToShow == categoryScrollView);
            profilePanel.SetActive(panelToShow == profilePanel);
            currentBasePanel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        else if (panelToShow == imageSelectPanel || panelToShow == favoritesPanel)
        {
            // 第二层面板
            HidePanelsAboveLayer2();
            imageSelectPanel.SetActive(panelToShow == imageSelectPanel);
            favoritesPanel.SetActive(panelToShow == favoritesPanel);
            currentLayer2Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        else if (panelToShow == difficultyPanel)
        {
            // 第三层面板
            HidePanelsAboveLayer3();
            difficultyPanel.SetActive(true);
            currentLayer3Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        else if (panelToShow == purchasePanel)
        {
            // 第四层面板
            HidePanelsAboveLayer4();
            purchasePanel.SetActive(true);
            currentLayer4Panel = panelToShow;
            panelToShow.transform.SetAsLastSibling();
        }
        else if (panelToShow == nameInputPanel)
        {
            // 姓名输入面板：隐藏所有其他面板
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

        // 更新个人信息或收藏面板内容
        if (panelToShow == profilePanel) UpdateProfileUI();
        if (panelToShow == favoritesPanel) PopulateFavoritesPanel();
    }

    // ==================== 面板隐藏辅助方法 ====================

    void HideOverlayPanels()
    {
        imageSelectPanel.SetActive(false);
        favoritesPanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentLayer2Panel = null;
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    void HidePanelsAboveLayer2()
    {
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    void HidePanelsAboveLayer3()
    {
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentLayer4Panel = null;
    }

    void HidePanelsAboveLayer4()
    {
        nameInputPanel.SetActive(false);
    }

    void HideAllPanels()
    {
        categoryScrollView.SetActive(false);
        profilePanel.SetActive(false);
        imageSelectPanel.SetActive(false);
        favoritesPanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentBasePanel = null;
        currentLayer2Panel = null;
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    // ==================== 分类按钮生成与预览 ====================

    /// <summary>
    /// 生成所有分类按钮，并启动预览图协程
    /// </summary>
    void GenerateCategoryButtons()
    {
        // 停止所有旧的预览协程
        foreach (var kvp in previewCoroutines)
        {
            if (kvp.Value != null)
                StopCoroutine(kvp.Value);
        }
        previewCoroutines.Clear();

        // 清空旧按钮
        foreach (Transform child in categoryScrollContent)
        {
            Destroy(child.gameObject);
        }

        // 设置 GridLayoutGroup 参数
        GridLayoutGroup grid = categoryScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
            grid = categoryScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(350, 350);
        grid.spacing = new Vector2(50, 50);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;  // 两列
        grid.childAlignment = TextAnchor.UpperCenter;

        // 遍历所有分类
        for (int i = 0; i < GameDataManager.Categories.Length; i++)
        {
            string category = GameDataManager.Categories[i];
            GameObject btnObj = Instantiate(categoryButtonPrefab, categoryScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Text label = btnObj.GetComponentInChildren<Text>();
            Image previewImage = btnObj.transform.Find("PreviewImage")?.GetComponent<Image>();

            // 设置按钮文本（分类名，所有分类已免费，不显示价格）
            bool unlocked = GameDataManager.IsCategoryUnlocked(category);
            if (label != null)
                label.text = unlocked ? category : $"{category}\n{GameDataManager.CategoryPrices[i]}金币";

            // 绑定点击事件
            int index = i;
            btn.onClick.AddListener(() => OnCategoryClicked(index));

            // 启动预览图渐变协程
            if (previewImage != null)
            {
                Coroutine coroutine = StartCoroutine(UpdateCategoryPreview(previewImage, category));
                previewCoroutines[btn] = coroutine;
            }
        }
    }

    /// <summary>
    /// 体力 UI 更新协程，每秒刷新一次
    /// </summary>
    IEnumerator UpdateStaminaUI()
    {
        while (true)
        {
            UpdateStaminaDisplay();
            yield return new WaitForSeconds(1f);
        }
    }

    void UpdateStaminaDisplay()
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

    /// <summary>
    /// 单个分类按钮的预览图渐变切换
    /// </summary>
    IEnumerator UpdateCategoryPreview(Image previewImage, string category)
    {
        if (previewImage == null) yield break;
        Sprite[] sprites = Resources.LoadAll<Sprite>("Art/" + category);
        if (sprites.Length == 0) yield break;

        while (previewImage != null)
        {
            Sprite newSprite = sprites[Random.Range(0, sprites.Length)];
            yield return StartCoroutine(FadeToSprite(previewImage, newSprite));
            if (previewImage == null) yield break;
            yield return new WaitForSeconds(previewChangeInterval);
        }
    }

    /// <summary>
    /// 图片渐变切换协程（淡出-换图-淡入）
    /// </summary>
    IEnumerator FadeToSprite(Image image, Sprite newSprite)
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

    // ==================== 分类点击与图片选择 ====================

    /// <summary>
    /// 分类按钮点击：直接打开图片选择面板（所有分类免费）
    /// </summary>
    void OnCategoryClicked(int index)
    {
        string category = GameDataManager.Categories[index];
        selectedCategory = category;
        OpenImageSelectPanel(category);
        ShowPanel(imageSelectPanel);
    }

    /// <summary>
    /// 填充图片选择面板（包含随机按钮和分类下所有图片）
    /// </summary>
    void OpenImageSelectPanel(string category)
    {
        // 清空旧按钮
        foreach (Transform child in imageScrollContent)
            Destroy(child.gameObject);

        // 设置 Grid
        GridLayoutGroup grid = imageScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
            grid = imageScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(350, 350);
        grid.spacing = new Vector2(50, 50);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 随机按钮
        GameObject randomBtnObj = Instantiate(imageButtonPrefab, imageScrollContent);
        Button randomBtn = randomBtnObj.GetComponent<Button>();
        Text randomLabel = randomBtnObj.GetComponentInChildren<Text>();
        Image randomImage = randomBtnObj.transform.Find("Image")?.GetComponent<Image>();
        if (randomLabel != null) randomLabel.text = "随机";
        if (randomImage != null)
        {
            randomImage.sprite = null;
            randomImage.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        }
        randomBtn.onClick.AddListener(() => OnImageClicked(-1));

        // 加载分类下所有图片
        Sprite[] sprites = Resources.LoadAll<Sprite>("Art/" + category);
        System.Array.Sort(sprites, (a, b) => string.Compare(a.name, b.name));

        for (int i = 0; i < sprites.Length; i++)
        {
            GameObject imgBtnObj = Instantiate(imageButtonPrefab, imageScrollContent);
            Button imgBtn = imgBtnObj.GetComponent<Button>();
            Image img = imgBtnObj.transform.Find("Image")?.GetComponent<Image>();
            Text label = imgBtnObj.GetComponentInChildren<Text>();

            if (img != null)
            {
                img.sprite = sprites[i];
                bool unlocked = GameDataManager.IsImageUnlocked(category, i);
                img.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.7f);
            }
            if (label != null)
                label.text = GameDataManager.IsImageUnlocked(category, i) ? "" : $"{GameDataManager.GetImagePrice(category, i)}金币";

            int imageIndex = i;
            imgBtn.onClick.AddListener(() => OnImageClicked(imageIndex));
        }
    }

    /// <summary>
    /// 图片点击：解锁则进入难度选择，未解锁则弹出购买
    /// </summary>
    void OnImageClicked(int imageIndex)
    {
        if (imageIndex == -1) // 随机
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

    // ==================== 购买逻辑 ====================

    void ConfirmPurchase()
    {
        // 如果 pendingPurchaseImageIndex == -1，表示购买分类（但当前所有分类免费，此分支基本不会触发）
        if (pendingPurchaseImageIndex == -1)
        {
            int price = GameDataManager.CategoryPrices[System.Array.IndexOf(GameDataManager.Categories, pendingPurchaseCategory)];
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
            // 购买图片
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

    /// <summary>
    /// 金币不足时文本闪烁红色两下
    /// </summary>
    IEnumerator FlashCoinTextRed()
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

    // ==================== 其他按钮事件 ====================

    void OnAdStaminaClicked()
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

    // 模拟广告方法，实际项目替换为真实广告 SDK
    void ShowRewardedAd(System.Action onSuccess, System.Action onFail)
    {
        onSuccess?.Invoke();
    }

    void OnDailyPuzzleClicked()
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

    void ShowConfirm(string message, System.Action onConfirm)
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

    void OnConfirmYes()
    {
        confirmPanel.SetActive(false);
        confirmAction?.Invoke();
    }

    // ==================== 游戏启动与个人信息 ====================

    void StartGame(int gridSize)
    {
        // 保存难度选择参数
        PlayerPrefs.SetString("SelectedCategory", selectedCategory);
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.SetInt("SelectedImageIndex", selectedImageIndex);
        PlayerPrefs.Save();

        // 显示加载面板并置顶
        loadingPanel.SetActive(true);
        loadingPanel.transform.SetAsLastSibling(); // 确保在 Canvas 最上层
        if (loadingText != null) loadingText.text = "加载中...";

        // 启动异步加载
        StartCoroutine(LoadGameAsync());
    }

    void UpdateCoinDisplay()
    {
        if (coinText != null)
            coinText.text = "金币：" + GameDataManager.Coins;
    }

    void OnNameConfirmed()
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
        ShowPanel(categoryScrollView);
    }

    void UpdateProfileUI()
    {
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

    // ==================== 收藏面板 ====================

    void PopulateFavoritesPanel()
    {
        foreach (Transform child in favoritesScrollContent)
            Destroy(child.gameObject);

        GridLayoutGroup grid = favoritesScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
            grid = favoritesScrollContent.gameObject.AddComponent<GridLayoutGroup>();
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

            Sprite[] sprites = Resources.LoadAll<Sprite>("Art/" + category);
            System.Array.Sort(sprites, (a, b) => string.Compare(a.name, b.name));
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
    IEnumerator LoadGameAsync()
    {
        float startTime = Time.realtimeSinceStartup;
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("GameScene");
        asyncLoad.allowSceneActivation = false; // 先不自动切换场景

        // 定义显示进度和实际加载进度
        float displayProgress = 0f;
        float realProgress = 0f;

        while (displayProgress < 1f || asyncLoad.progress < 0.9f)
        {
            // 实际加载进度（0~0.9 映射到 0~1）
            realProgress = Mathf.Clamp01(asyncLoad.progress / 0.9f);

            // 基于时间的显示进度：至少3秒到100%
            float timeProgress = Mathf.Clamp01((Time.realtimeSinceStartup - startTime) / 3f);

            // 显示进度取两者较小值，保证不会超过实际进度，也不会早于3秒到100%
            displayProgress = Mathf.Min(realProgress, timeProgress);

            // 更新文字
            if (loadingText != null)
                loadingText.text = $"加载中... {Mathf.RoundToInt(displayProgress * 100)}%";
            if (loadingSlider != null)
            {
                loadingSlider.value = displayProgress;
            }
            yield return null;
        }

        // 确保显示100%后再切换
        if (loadingText != null)
            loadingText.text = "加载中... 100%";

        // 激活场景
        asyncLoad.allowSceneActivation = true;
    }
}