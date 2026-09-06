using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class MainMenuManager : MonoBehaviour
{
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

    private string selectedCategory;
    private int selectedImageIndex = -1;
    private string pendingPurchaseCategory;
    private int pendingPurchaseImageIndex;

    private Dictionary<Button, Coroutine> previewCoroutines = new Dictionary<Button, Coroutine>();

    void Awake()
    {
        // 测试用：编辑器下重置数据（可选）
        // GameDataManager.ResetForEditor();
    }

    void Start()
    {
        // 确保 CategoryScrollView 位于最底层，其他面板隐藏
        categoryScrollView.SetActive(true);
        //categoryScrollView.transform.SetAsFirstSibling();
        categoryScrollView.transform.SetAsLastSibling(); // 将 CategoryScrollView 移到最上层
        imageSelectPanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);

        // 按钮事件
        easyButton.onClick.AddListener(() => StartGame(2));
        normalButton.onClick.AddListener(() => StartGame(8));
        hardButton.onClick.AddListener(() => StartGame(10));

        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
        cancelPurchaseButton.onClick.AddListener(() => ShowPanel(null));          // 关闭购买面板，回到分类
        closeImagePanelButton.onClick.AddListener(() => ShowPanel(null));         // 关闭图片面板，回到分类

        UpdateCoinDisplay();
        GenerateCategoryButtons();
    }

    void OnEnable()
    {
        UpdateCoinDisplay();
    }

    void ShowPanel(GameObject panelToShow)
    {
        // 始终不隐藏 CategoryScrollView
        imageSelectPanel.SetActive(false);
        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);

        if (panelToShow != null && panelToShow != categoryScrollView)
        {
            panelToShow.SetActive(true);
            panelToShow.transform.SetAsLastSibling();   // 确保面板在最上层
        }
    }

    void GenerateCategoryButtons()
    {
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

    IEnumerator UpdateCategoryPreview(Image previewImage, string category)
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>("Art/" + category);
        if (sprites.Length == 0) yield break;

        while (true)
        {
            Sprite newSprite = sprites[Random.Range(0, sprites.Length)];
            yield return StartCoroutine(FadeToSprite(previewImage, newSprite));
            yield return new WaitForSeconds(previewChangeInterval);
        }
    }

    IEnumerator FadeToSprite(Image image, Sprite newSprite)
    {
        float elapsed = 0f;
        Color startColor = image.color;
        Color transparentColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(startColor, transparentColor, elapsed / fadeDuration);
            yield return null;
        }

        image.sprite = newSprite;

        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(transparentColor, startColor, elapsed / fadeDuration);
            yield return null;
        }
        image.color = startColor;
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
                GenerateCategoryButtons();
                ShowPanel(null);   // 隐藏所有弹出面板，露出分类
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
}