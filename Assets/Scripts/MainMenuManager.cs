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
/// 个人信息、收藏、每日拼图、看广告恢复体力、购买、上传图片管理等功能。
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // ==================== UI 引用 ====================

    [Header("修改名字")]
    public Button changeNameButton;                 // 个人信息面板中的修改名字按钮

    [Header("上传图片")]
    public Button uploadButtonInProfile;            // 个人信息面板中的“上传管理”按钮
    public GameObject uploadManagePanel;            // 上传管理面板（显示已上传图片并提供删除）
    public RectTransform uploadScrollContent;       // 上传管理面板的 Content
    public Button uploadCloseButton;                // 关闭上传管理面板按钮
    public Button addImageButton;                   // 上传管理面板中的“添加图片”按钮

    [Header("每日拼图")]
    public Button dailyPuzzleButton;                // 每日拼图入口按钮

    [Header("加载面板")]
    public GameObject loadingPanel;                 // 加载面板物体
    public Text loadingText;                        // 加载提示文字
    public Slider loadingSlider;                    // 加载进度条（可选）

    [Header("通用确认弹窗")]
    public GameObject confirmPanel;                 // 确认弹窗面板
    public Text confirmText;                        // 弹窗提示文字
    public Button confirmYesButton;                 // 确定按钮
    public Button confirmNoButton;                  // 取消按钮

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

    private GameObject currentBasePanel;            // 当前基础面板
    private GameObject currentLayer2Panel;          // 当前第二层面板
    private GameObject currentLayer3Panel;          // 当前第三层面板
    private GameObject currentLayer4Panel;          // 当前第四层面板

    private Dictionary<Button, Coroutine> previewCoroutines = new Dictionary<Button, Coroutine>();

    private System.Action confirmAction;            // 确认弹窗回调
    private GameObject panelAfterNameChange;        // 名字修改成功后显示的面板

    void Awake()
    {
        // 测试时可启用此行清空数据，正式版请注释掉
        // GameDataManager.ResetForEditor();
    }

    void Start()
    {
        // ========== 绑定按钮事件 ==========
        changeNameButton.onClick.AddListener(OnChangeNameClicked);
        dailyPuzzleButton.onClick.AddListener(OnDailyPuzzleClicked);

        // 上传相关
        uploadButtonInProfile.onClick.AddListener(OpenUploadManagePanel);  // 打开上传管理面板
        uploadCloseButton.onClick.AddListener(() => ShowPanel(profilePanel));
        addImageButton.onClick.AddListener(OnUploadButtonClicked);        // 添加新图片

        // 确认弹窗
        confirmYesButton.onClick.AddListener(OnConfirmYes);
        confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);

        // 底部按钮
        profileButton.onClick.AddListener(() => ShowPanel(profilePanel));
        categoryButton.onClick.AddListener(() => ShowPanel(categoryScrollView));

        // 收藏面板
        favoritesButton.onClick.AddListener(() => ShowPanel(favoritesPanel));
        favoritesCloseButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? profilePanel));

        // 姓名确认
        nameConfirmButton.onClick.AddListener(OnNameConfirmed);

        // 难度取消
        difficultyCancelButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null)
                ShowPanel(currentLayer2Panel);
            else
                ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        // 初始化体力系统
        GameDataManager.InitStaminaSystem();

        // 启动体力更新协程
        StartCoroutine(UpdateStaminaUI());

        // 初始显示
        if (string.IsNullOrEmpty(GameDataManager.PlayerName))
        {
            panelAfterNameChange = categoryScrollView;
            ShowPanel(nameInputPanel);
        }
        else
        {
            ShowPanel(categoryScrollView);
        }

        // 难度按钮（测试用 2x2，正式改回 6）
        easyButton.onClick.AddListener(() => StartGame(2));
        normalButton.onClick.AddListener(() => StartGame(8));
        hardButton.onClick.AddListener(() => StartGame(10));

        // 看广告恢复体力
        adStaminaButton.onClick.AddListener(OnAdStaminaClicked);

        // 购买面板
        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
        cancelPurchaseButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null) ShowPanel(currentLayer2Panel);
            else ShowPanel(currentBasePanel ?? categoryScrollView);
        });

        // 关闭图片选择面板
        closeImagePanelButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? categoryScrollView));

        // 更新显示
        UpdateCoinDisplay();
        GenerateCategoryButtons();
        loadingPanel.SetActive(false);
    }

    void OnEnable()
    {
        UpdateCoinDisplay();
        UpdateProfileUI();
    }

    // ==================== 面板管理 ====================

    void ShowPanel(GameObject panelToShow)
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
        else if (panelToShow == imageSelectPanel || panelToShow == favoritesPanel || panelToShow == uploadManagePanel)
        {
            HidePanelsAboveLayer2();
            imageSelectPanel.SetActive(panelToShow == imageSelectPanel);
            favoritesPanel.SetActive(panelToShow == favoritesPanel);
            uploadManagePanel.SetActive(panelToShow == uploadManagePanel);
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
            if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = 999; }
        }

        // 更新内容
        if (panelToShow == profilePanel) UpdateProfileUI();
        if (panelToShow == favoritesPanel) PopulateFavoritesPanel();
        if (panelToShow == uploadManagePanel) PopulateUploadManagePanel();
    }

    void HideOverlayPanels()
    {
        imageSelectPanel.SetActive(false);
        favoritesPanel.SetActive(false);
        uploadManagePanel.SetActive(false);
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
        uploadManagePanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        nameInputPanel.SetActive(false);
        currentBasePanel = null;
        currentLayer2Panel = null;
        currentLayer3Panel = null;
        currentLayer4Panel = null;
    }

    // ==================== 分类按钮生成 ====================

    void GenerateCategoryButtons()
    {
        foreach (var kvp in previewCoroutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        previewCoroutines.Clear();

        foreach (Transform child in categoryScrollContent) Destroy(child.gameObject);

        GridLayoutGroup grid = categoryScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = categoryScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(350, 350);
        grid.spacing = new Vector2(50, 50);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 普通分类
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
        if (uploadPreview != null) uploadPreview.sprite = null; // 可设置默认图标
        uploadBtn.onClick.AddListener(() => OnCategoryClicked(GameDataManager.Categories.Length));
    }

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
        if (staminaText != null) staminaText.text = $"体力：{GameDataManager.Stamina}/{GameDataManager.MaxStamina}";
        if (staminaSlider != null)
        {
            staminaSlider.maxValue = GameDataManager.MaxStamina;
            staminaSlider.value = GameDataManager.Stamina;
            staminaSlider.interactable = false;
        }
    }

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

    IEnumerator FadeToSprite(Image image, Sprite newSprite)
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

    // ==================== 分类点击与图片选择 ====================

    void OnCategoryClicked(int index)
    {
        if (index == GameDataManager.Categories.Length) // 上传分类
        {
            selectedCategory = GameDataManager.UploadCategory;
            OpenImageSelectPanel(GameDataManager.UploadCategory);
            ShowPanel(imageSelectPanel);
            return;
        }

        string category = GameDataManager.Categories[index];
        selectedCategory = category;
        OpenImageSelectPanel(category);
        ShowPanel(imageSelectPanel);
    }

    void OpenImageSelectPanel(string category)
    {
        foreach (Transform child in imageScrollContent) Destroy(child.gameObject);

        GridLayoutGroup grid = imageScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = imageScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(350, 350);
        grid.spacing = new Vector2(50, 50);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        List<Sprite> sprites = new List<Sprite>();
        List<string> imageNames = new List<string>();

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
        else
        {
            Sprite[] loadedSprites = Resources.LoadAll<Sprite>("Art/" + category);
            System.Array.Sort(loadedSprites, (a, b) => string.Compare(a.name, b.name));
            sprites.AddRange(loadedSprites);
            for (int i = 0; i < sprites.Count; i++) imageNames.Add(sprites[i].name);
        }

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
                if (img != null) img.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.7f);
                if (label != null) label.text = unlocked ? "" : $"{GameDataManager.GetImagePrice(category, i)}金币";
            }

            int imageIndex = i;
            imgBtn.onClick.AddListener(() => OnImageClicked(imageIndex));
        }

        imageSelectPanel.SetActive(true);
    }

    void OnImageClicked(int imageIndex)
    {
        // 上传分类：直接进入难度选择
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            selectedImageIndex = imageIndex;
            ShowPanel(difficultyPanel);
            return;
        }

        // 普通分类
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

    // ==================== 购买逻辑 ====================

    void ConfirmPurchase()
    {
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

    // ==================== 上传图片管理 ====================

    void OpenUploadManagePanel()
    {
        PopulateUploadManagePanel();
        ShowPanel(uploadManagePanel);
    }

    void PopulateUploadManagePanel()
    {
        // 清空旧按钮
        foreach (Transform child in uploadScrollContent)
            Destroy(child.gameObject);

        // ===== 添加 GridLayoutGroup =====
        GridLayoutGroup grid = uploadScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
            grid = uploadScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 200);       // 图片按钮大小，可调整
        grid.spacing = new Vector2(20, 20);          // 间隔
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;                    // 每行3个
        grid.childAlignment = TextAnchor.UpperCenter;

        // ===== 添加 ContentSizeFitter =====
        ContentSizeFitter fitter = uploadScrollContent.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = uploadScrollContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;   // 高度自动扩展
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // 宽度不自动调整

        // 加载已上传图片并生成按钮

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

            // 点击删除
            string fileName = file;
            btn.onClick.AddListener(() =>
            {
                GameDataManager.RemoveUploadedImage(fileName);
                PopulateUploadManagePanel(); // 刷新列表
            });

            Text label = btnObj.GetComponentInChildren<Text>();
            if (label != null) label.text = "删除";
        }
    }

    void OnUploadButtonClicked()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("选择图片", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path))
        {
            ProcessUploadedImage(path);
        }
#else
        // 移动端需要 NativeGallery 插件，请自行导入
        // NativeGallery.Permission permission = NativeGallery.GetImageFromGallery((path) => { if (path != null) ProcessUploadedImage(path); }, "选择图片", "image/*");
        Debug.LogWarning("移动端请导入 NativeGallery 插件以启用相册选择");
#endif
    }

    void ProcessUploadedImage(string sourcePath)
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
            PopulateUploadManagePanel(); // 刷新管理面板
        }
        catch (Exception e)
        {
            Debug.LogError("上传图片失败: " + e.Message);
        }
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

    void ShowRewardedAd(System.Action onSuccess, System.Action onFail)
    {
        onSuccess?.Invoke(); // 模拟广告成功
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
        PlayerPrefs.SetString("SelectedCategory", selectedCategory);
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.SetInt("SelectedImageIndex", selectedImageIndex);
        PlayerPrefs.Save();

        loadingPanel.SetActive(true);
        loadingPanel.transform.SetAsLastSibling();
        if (loadingText != null) loadingText.text = "加载中...";
        StartCoroutine(LoadGameAsync());
    }

    IEnumerator LoadGameAsync()
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

            if (loadingText != null) loadingText.text = $"加载中... {Mathf.RoundToInt(displayProgress * 100)}%";
            if (loadingSlider != null) loadingSlider.value = displayProgress;
            yield return null;
        }

        if (loadingText != null) loadingText.text = "加载中... 100%";
        asyncLoad.allowSceneActivation = true;
    }

    void UpdateCoinDisplay()
    {
        if (coinText != null) coinText.text = "金币：" + GameDataManager.Coins;
    }

    void OnChangeNameClicked()
    {
        panelAfterNameChange = profilePanel;
        nameInputField.text = GameDataManager.PlayerName;
        ShowPanel(nameInputPanel);
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
        ShowPanel(panelAfterNameChange);
    }

    void UpdateProfileUI()
    {
        if (profileNameText != null) profileNameText.text = GameDataManager.PlayerName;
        if (profileLevelText != null) profileLevelText.text = $"等级 {GameDataManager.Level}  {GameDataManager.Experience}/{GameDataManager.GetRequiredExperience(GameDataManager.Level)}";
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
        foreach (Transform child in favoritesScrollContent) Destroy(child.gameObject);

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
            if (label != null) label.text = unlocked ? "" : $"{GameDataManager.GetImagePrice(category, imageIndex)}金币";

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
}