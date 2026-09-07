using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class MainMenuManager : MonoBehaviour
{
    [Header("广告恢复")]
    public Button adStaminaButton;   // 看广告恢复体力按钮

    [Header("体力显示")]
    public Text staminaText;         // 显示体力/上限
    public Slider staminaSlider;     // 可选，体力条

    [Header("分类 ScrollView")]
    public GameObject categoryScrollView;             // 分类 ScrollView 物体（整个 ScrollView）
    public RectTransform categoryScrollContent;      // 分类按钮的父物体（Content）
    public GameObject categoryButtonPrefab;          // 分类按钮预制体

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

    [Header("购买面板")]
    public GameObject purchasePanel;
    public Text purchaseText;
    public Button confirmPurchaseButton;
    public Button cancelPurchaseButton;

    [Header("金币显示")]
    public Text coinText;

    [Header("个人信息")]
    public GameObject profilePanel;
    public Text profileNameText;
    public Text profileLevelText;
    public Slider experienceSlider;
    public Button favoritesButton;
   // public Button profileBackButton;

    [Header("姓名输入面板")]
    public GameObject nameInputPanel;
    public InputField nameInputField;
    public Button nameConfirmButton;

    [Header("我的收藏面板")]
    public GameObject favoritesPanel;
    public RectTransform favoritesScrollContent;
    public Button favoritesCloseButton;

    [Header("底部按钮")]
    public Button profileButton;      // 个人信息按钮
    public Button categoryButton;     // CategoryScrollView按钮

    [Header("难度面板取消按钮")]
    public Button difficultyCancelButton;

 

    private string selectedCategory;
    private int selectedImageIndex = -1;
    private string pendingPurchaseCategory;
    private int pendingPurchaseImageIndex;
    private GameObject currentBasePanel;    // 当前基础面板：categoryScrollView 或 profilePanel
    private GameObject currentLayer2Panel;  // 当前第二层面板：imageSelectPanel 或 favoritesPanel
    private GameObject currentLayer3Panel;  // 当前第三层面板：difficultyPanel
    private GameObject currentLayer4Panel;  // 当前第四层面板：purchasePanel

    private Dictionary<Button, Coroutine> previewCoroutines = new Dictionary<Button, Coroutine>();

    void Awake()
    {
        // 测试用：编辑器下重置数据（可选）
        // GameDataManager.ResetForEditor();
    }
    
    void Start()
    {
        // 底部按钮
        profileButton.onClick.AddListener(() => ShowPanel(profilePanel));
        categoryButton.onClick.AddListener(() => ShowPanel(categoryScrollView));
//profileBackButton.onClick.AddListener(() => ShowPanel(categoryScrollView));
        favoritesButton.onClick.AddListener(() => ShowPanel(favoritesPanel));
        favoritesCloseButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? profilePanel));

        // 姓名确认
        nameConfirmButton.onClick.AddListener(OnNameConfirmed);

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
            ShowPanel(nameInputPanel);
        }
        else
        {
            ShowPanel(categoryScrollView);
        }
        //// 确保 CategoryScrollView 位于最底层，其他面板隐藏
        //categoryScrollView.SetActive(true);
        ////categoryScrollView.transform.SetAsFirstSibling();
        //categoryScrollView.transform.SetAsLastSibling(); // 将 CategoryScrollView 移到最上层
        //imageSelectPanel.SetActive(false);
        //difficultyPanel.SetActive(false);
        //purchasePanel.SetActive(false);

        // 按钮事件
        easyButton.onClick.AddListener(() => StartGame(2));
        normalButton.onClick.AddListener(() => StartGame(8));
        hardButton.onClick.AddListener(() => StartGame(10));

        adStaminaButton.onClick.AddListener(OnAdStaminaClicked);

        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
        cancelPurchaseButton.onClick.AddListener(() =>
        {
            if (currentLayer2Panel != null) ShowPanel(currentLayer2Panel);
            else ShowPanel(currentBasePanel ?? categoryScrollView);
        });
        closeImagePanelButton.onClick.AddListener(() => ShowPanel(currentBasePanel ?? categoryScrollView));

        UpdateCoinDisplay();
        GenerateCategoryButtons();
    }

    void OnEnable()
    {
        UpdateCoinDisplay();
        UpdateProfileUI();   // 添加这一行，确保数据刷新
    }

    void ShowPanel(GameObject panelToShow)
    {
        // 如果目标为空，仅隐藏所有覆盖面板（保留基础面板）
        if (panelToShow == null)
        {
            HideOverlayPanels();
            return;
        }

        // 判断面板类型
        if (panelToShow == categoryScrollView || panelToShow == profilePanel)
        {
            // 基础面板互斥
            HideOverlayPanels(); // 隐藏所有覆盖面板
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
            if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = 999; }
        }

        // 更新个人信息或收藏面板内容
        if (panelToShow == profilePanel) UpdateProfileUI();
        if (panelToShow == favoritesPanel) PopulateFavoritesPanel();
    }

    // 辅助方法
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

    void GenerateCategoryButtons()
    {
        // 停止所有旧协程
        foreach (var kvp in previewCoroutines)
        {
            if (kvp.Value != null)
                StopCoroutine(kvp.Value);
        }
        previewCoroutines.Clear();
        foreach (Transform child in categoryScrollContent)
        {
            Destroy(child.gameObject);
        }
        previewCoroutines.Clear();

        GridLayoutGroup grid = categoryScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = categoryScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        }
        grid.cellSize = new Vector2(350, 350);
        grid.spacing = new Vector2(50, 50);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        for (int i = 0; i < GameDataManager.Categories.Length; i++)
        {
            string category = GameDataManager.Categories[i];
            GameObject btnObj = Instantiate(categoryButtonPrefab, categoryScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Text label = btnObj.GetComponentInChildren<Text>();
            Image previewImage = btnObj.transform.Find("PreviewImage")?.GetComponent<Image>();

            bool unlocked = GameDataManager.IsCategoryUnlocked(category);
            if (label != null)
            {
                label.text = unlocked ? category : $"{category}\n{GameDataManager.CategoryPrices[i]}金币";
            }

            int index = i;
            btn.onClick.AddListener(() => OnCategoryClicked(index));

            if (previewImage != null)
            {
                Coroutine coroutine = StartCoroutine(UpdateCategoryPreview(previewImage, category));
                previewCoroutines[btn] = coroutine;
            }
        }
    }
    IEnumerator UpdateStaminaUI()
    {
        while (true)
        {
            UpdateStaminaDisplay();
            yield return new WaitForSeconds(1f); // 每秒刷新一次
        }
    }

    void UpdateStaminaDisplay()
    {
        if (staminaText != null)
        {
            staminaText.text = $"体力：{GameDataManager.Stamina}/{GameDataManager.MaxStamina}";
        }
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

        while (previewImage != null)   // 当 previewImage 不为空时循环
        {
            Sprite newSprite = sprites[Random.Range(0, sprites.Length)];
            yield return StartCoroutine(FadeToSprite(previewImage, newSprite));
            if (previewImage == null) yield break;    // 如果协程中图片被销毁，退出
            yield return new WaitForSeconds(previewChangeInterval);
        }
    }

    IEnumerator FadeToSprite(Image image, Sprite newSprite)
    {
        if (image == null) yield break;

        float elapsed = 0f;
        Color startColor = image.color;
        Color transparentColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        // 淡出阶段
        while (elapsed < fadeDuration)
        {
            if (image == null) yield break;   // 每帧检查图片是否被销毁
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(startColor, transparentColor, elapsed / fadeDuration);
            yield return null;
        }

        if (image == null) yield break;       // 更换图片前检查
        image.sprite = newSprite;

        // 淡入阶段
        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            if (image == null) yield break;   // 每帧检查
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(transparentColor, startColor, elapsed / fadeDuration);
            yield return null;
        }
        if (image != null) image.color = startColor;   // 最终颜色
    }

    void OnCategoryClicked(int index)
    {
        string category = GameDataManager.Categories[index];
        if (GameDataManager.IsCategoryUnlocked(category))
        {
            selectedCategory = category;
            OpenImageSelectPanel(category);
            ShowPanel(imageSelectPanel);      // 显示图片面板，关闭其他
        }
        else
        {
            selectedCategory = category;
            pendingPurchaseCategory = category;
            pendingPurchaseImageIndex = -1;
            purchaseText.text = $"是否花费 {GameDataManager.CategoryPrices[index]} 金币解锁 {category} 分类？";
            ShowPanel(purchasePanel);         // 显示购买面板
        }
    }
    void OnAdStaminaClicked()
    {
        // 禁用按钮，防止重复点击（可选）
        adStaminaButton.interactable = false;

        // 调用广告（这里用模拟方法，实际接入时替换为真实广告SDK）
        ShowRewardedAd(() =>
        {
            // 广告观看成功：奖励4体力+2金币
            GameDataManager.AddStamina(4);
            GameDataManager.AddCoins(2);
            UpdateStaminaDisplay();
            UpdateCoinDisplay();

            Debug.Log("观看广告成功，获得4体力、2金币");
            adStaminaButton.interactable = true; // 恢复按钮
        }, () =>
        {
            // 广告观看失败或取消
            Debug.Log("广告未完成，无奖励");
            adStaminaButton.interactable = true;
        });
    }

    // 模拟广告方法（实际项目请替换为真实广告代码）
    void ShowRewardedAd(System.Action onSuccess, System.Action onFail)
    {
        // 这里简单直接调用成功回调，模拟广告观看完成
        // 替换为：UnityAds.ShowRewardedAd(onSuccess, onFail) 等
        onSuccess?.Invoke();
    }
    void OpenImageSelectPanel(string category)
    {
        foreach (Transform child in imageScrollContent)
        {
            Destroy(child.gameObject);
        }

        GridLayoutGroup grid = imageScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = imageScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        }
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

        // 图片按钮
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
            {
                label.text = GameDataManager.IsImageUnlocked(category, i) ? "" : $"{GameDataManager.GetImagePrice(category, i)}金币";
            }

            int imageIndex = i;
            imgBtn.onClick.AddListener(() => OnImageClicked(imageIndex));
        }
    }

    void OnImageClicked(int imageIndex)
    {
        if (imageIndex == -1)
        {
            selectedImageIndex = -1;
            ShowPanel(difficultyPanel);       // 显示难度面板
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
                // 购买分类成功
                GameDataManager.UnlockCategory(pendingPurchaseCategory);
                UpdateCoinDisplay();
                //GenerateCategoryButtons();
                ShowPanel(categoryScrollView);   // 显示分类滚动视图
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
                ShowPanel(imageSelectPanel);     // 刷新图片面板并显示
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
        if (coinText == null) yield break;
        Color originalColor = coinText.color;
        coinText.color = Color.red;
        yield return new WaitForSeconds(0.2f);
        coinText.color = originalColor;
        yield return new WaitForSeconds(0.2f);
        coinText.color = Color.red;
        yield return new WaitForSeconds(0.2f);
        coinText.color = originalColor;
    }

    void StartGame(int gridSize)
    {
        PlayerPrefs.SetString("SelectedCategory", selectedCategory);
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.SetInt("SelectedImageIndex", selectedImageIndex);
        PlayerPrefs.Save();

        SceneManager.LoadScene("GameScene");
    }

    void UpdateCoinDisplay()
    {
        if (coinText != null)
            coinText.text = "金币：" + GameDataManager.Coins;
    }
    void OnNameConfirmed()
    {
        string name = nameInputField.text.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            GameDataManager.PlayerName = name;
            ShowPanel(categoryScrollView);
        }
    }
    void UpdateProfileUI()
    {
        Debug.Log("更新个人信息 UI");
        if (profileNameText != null) profileNameText.text = GameDataManager.PlayerName;
        if (profileLevelText != null)
        {
            profileLevelText.text = $"等级 {GameDataManager.Level}  {GameDataManager.Experience}/{GameDataManager.GetRequiredExperience(GameDataManager.Level)}";
        }

        if (experienceSlider != null)
        {
            experienceSlider.maxValue = GameDataManager.GetRequiredExperience(GameDataManager.Level);
            experienceSlider.value = GameDataManager.Experience;
            experienceSlider.interactable = false;
        }
    }
    void PopulateFavoritesPanel()
    {
        // 清空
        foreach (Transform child in favoritesScrollContent) Destroy(child.gameObject);

        // 设置Grid
        GridLayoutGroup grid = favoritesScrollContent.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = favoritesScrollContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(150, 150);
        grid.spacing = new Vector2(10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;

        List<string> favorites = GameDataManager.GetFavorites();
        foreach (string fav in favorites)
        {
            string[] parts = fav.Split('_');
            if (parts.Length != 2) continue;
            string category = parts[0];
            int imageIndex;
            if (!int.TryParse(parts[1], out imageIndex)) continue;

            // 加载对应图片
            Sprite[] sprites = Resources.LoadAll<Sprite>("Art/" + category);
            System.Array.Sort(sprites, (a, b) => string.Compare(a.name, b.name));
            if (imageIndex < 0 || imageIndex >= sprites.Length) continue;

            GameObject btnObj = Instantiate(imageButtonPrefab, favoritesScrollContent);
            Button btn = btnObj.GetComponent<Button>();
            Image img = btnObj.transform.Find("Image")?.GetComponent<Image>();
            if (img != null) img.sprite = sprites[imageIndex];

            string cat = category; int idx = imageIndex;
            btn.onClick.AddListener(() => {
                selectedCategory = cat;
                selectedImageIndex = idx;
                ShowPanel(difficultyPanel);
            });
        }
    }
}