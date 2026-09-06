using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class MainMenuManager : MonoBehaviour
{
    [Header("分类按钮")]
    public Button[] categoryButtons;          // 顺序与 GameDataManager.Categories 一致
    public Text[] categoryButtonTexts;        // 可选，用于更新按钮文字

    [Header("分类预览设置")]
    public Image[] categoryPreviewImages;      // 与分类按钮一一对应
    public float previewChangeInterval = 3f;   // 图片切换间隔
    public float fadeDuration = 0.5f;          // 渐变时间

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

    public Text coinText;

    private string selectedCategory;          // 当前选择的分类
    private int selectedPrice;                // 当前要购买的价格

    void Awake()
    {
        // 仅在编辑器下重置数据，方便测试
        //*****************************************************
        //  GameDataManager.ResetForEditor();
    }

    void Start()
    {
        // 初始化按钮状态
        UpdateCategoryButtons();

        // 难度按钮事件
        easyButton.onClick.AddListener(() => StartGame(2));
        normalButton.onClick.AddListener(() => StartGame(8));
        hardButton.onClick.AddListener(() => StartGame(10));

        // 购买面板
        confirmPurchaseButton.onClick.AddListener(ConfirmPurchase);
        cancelPurchaseButton.onClick.AddListener(() => purchasePanel.SetActive(false));

        difficultyPanel.SetActive(false);
        purchasePanel.SetActive(false);
        UpdateCoinDisplay();
        // 启动预览图更新协程
        StartCoroutine(UpdateCategoryPreviews());
    }

    void OnEnable()
    {
        UpdateCoinDisplay();
    }

    void UpdateCategoryButtons()
    {
        for (int i = 0; i < categoryButtons.Length; i++)
        {
            string category = GameDataManager.Categories[i];
            bool unlocked = GameDataManager.IsCategoryUnlocked(category);

            // 设置按钮可交互（未解锁也可点击，用于弹出购买）
            categoryButtons[i].interactable = true;

            // 更新文本：如果未解锁显示价格
            if (categoryButtonTexts.Length > i && categoryButtonTexts[i] != null)
            {
                categoryButtonTexts[i].text = unlocked ? category : $"{category}\n{GameDataManager.CategoryPrices[i]}金币";
            }

            // 保存索引以便在回调中使用
            int index = i;
            categoryButtons[i].onClick.RemoveAllListeners();
            categoryButtons[i].onClick.AddListener(() => OnCategoryClicked(index));
        }
    }

    void OnCategoryClicked(int index)
    {
        string category = GameDataManager.Categories[index];
        if (GameDataManager.IsCategoryUnlocked(category))
        {
            // 已解锁：显示难度面板
            selectedCategory = category;
            difficultyPanel.SetActive(true);
        }
        else
        {
            // 未解锁：显示购买面板
            selectedCategory = category;
            selectedPrice = GameDataManager.CategoryPrices[index];
            purchaseText.text = $"是否花费 {selectedPrice} 金币解锁 {category} 分类？";
            purchasePanel.SetActive(true);
        }
    }

    void ConfirmPurchase()
    {
        if (GameDataManager.SpendCoins(selectedPrice))
        {
            GameDataManager.UnlockCategory(selectedCategory);
            UpdateCategoryButtons();
            purchasePanel.SetActive(false);
            // 可以添加购买成功提示
            Debug.Log($"解锁 {selectedCategory} 成功！");
        }
        else
        {
            purchaseText.text = "金币不足！";
            // 可选：短暂显示后恢复
        }
    }

    void StartGame(int gridSize)
    {
        // 保存分类和难度供游戏场景使用
        PlayerPrefs.SetString("SelectedCategory", selectedCategory);
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.Save();

        // 隐藏难度面板
        difficultyPanel.SetActive(false);

        // 加载游戏场景
        SceneManager.LoadScene("GameScene");
    }
    void UpdateCoinDisplay()
    {
        if (coinText != null)
            coinText.text = "金币：" + GameDataManager.Coins;
    }

    // 协程：定期为每个分类按钮更新预览图（渐变切换）
    IEnumerator UpdateCategoryPreviews()
    {
        while (true)
        {
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                string category = GameDataManager.Categories[i];
                Sprite[] sprites = Resources.LoadAll<Sprite>("Art/" + category);
                if (sprites.Length > 0 && i < categoryPreviewImages.Length)
                {
                    Sprite newSprite = sprites[Random.Range(0, sprites.Length)];
                    yield return StartCoroutine(FadeToSprite(categoryPreviewImages[i], newSprite));
                }
            }
            yield return new WaitForSeconds(previewChangeInterval);
        }
    }

    // 渐变切换图片
    IEnumerator FadeToSprite(Image image, Sprite newSprite)
    {
        // 淡出
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

        // 淡入
        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(transparentColor, startColor, elapsed / fadeDuration);
            yield return null;
        }
        image.color = startColor;
    }
}