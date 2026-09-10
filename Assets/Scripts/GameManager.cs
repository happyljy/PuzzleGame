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
    public static GameManager Instance;

    // ==================== UI 引用 ====================
    [Header("音效")]
    public AudioClip victorySound;      // 胜利音效
    public AudioClip dailyCompleteSound; // 每日拼图全部完成音效（可选）
    [Header("UI References")]
    public Button favoriteButton;
    public Button nextImageButton;
    public Button prevImageButton;
    public Button backButton;
    public Button hintButton;
    public Button returnButton;
    public RectTransform listContent;
    public RectTransform puzzleArea;
    public GameObject victoryPanel;
    public GameObject piecePrefab;
    public Text rewardText;

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
    // GameManager.cs
    public int LockedCount => lockedCount;
    public int TotalPieces => totalPieces;
    // ==================== 私有状态 ====================

    private bool isDailyPuzzle = false;
    private int currentImageIndex = -1;
    private string selectedCategory;
    private int selectedImageIndex = -1;
    private int gridSize = 2; // 测试用，正式可改回6
    private float timeRemaining;
    private bool isVictory = false;

    private RectTransform puzzleContent;
    private float currentZoom = 1f;
    private Vector2 contentOffset;
    private float maxZoom = 2f;
    private float minZoom = 1f;

    private Sprite[] allSprites;
    private Sprite chosenSprite;
    private int rows, cols;
    private List<PuzzlePiece> activePieces = new List<PuzzlePiece>();
    private int totalPieces;
    private int lockedCount = 0;

    private Image hintImage;
    private bool isMultiTouch = false;
    public bool IsMultiTouch => isMultiTouch;

    private System.Action confirmAction;
    private int dailyPuzzleCurrentIndex = -1;

    void Awake()
    {
        Instance = this;

        favoriteButton.onClick.AddListener(ToggleFavorite);
        nextImageButton.onClick.AddListener(NextImage);
        prevImageButton.onClick.AddListener(PrevImage);
        backButton.onClick.AddListener(BackToMenu);
        returnButton.onClick.AddListener(ReturnUnlockedPieces);

        EventTrigger trigger = hintButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = hintButton.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry pointerDownEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDownEntry.callback.AddListener((data) => { ShowHint(); });
        trigger.triggers.Add(pointerDownEntry);
        EventTrigger.Entry pointerUpEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUpEntry.callback.AddListener((data) => { HideHint(); });
        trigger.triggers.Add(pointerUpEntry);

        confirmYesButton.onClick.AddListener(OnConfirmYes);
        confirmNoButton.onClick.AddListener(() => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);

        if (puzzleArea.GetComponent<RectMask2D>() == null)
            puzzleArea.gameObject.AddComponent<RectMask2D>();

        victoryPanel.SetActive(false);
    }

    void Start()
    {
        isDailyPuzzle = PlayerPrefs.GetInt("IsDailyPuzzle", 0) == 1;

        if (isDailyPuzzle)
        {
            List<string> images = GameDataManager.GetDailyPuzzleImages();
            List<int> difficulties = GameDataManager.GetDailyPuzzleDifficulties();

            if (images.Count == 0)
            {
                Debug.LogError("每日拼图数据为空");
                BackToMenu();
                return;
            }

            dailyPuzzleCurrentIndex = 0; // 从第一张开始，或者可根据需要选择
            if (dailyPuzzleCurrentIndex >= images.Count) dailyPuzzleCurrentIndex = 0;

            string[] parts = images[dailyPuzzleCurrentIndex].Split('_');
            selectedCategory = parts[0];
            selectedImageIndex = int.Parse(parts[1]);
            gridSize = difficulties[dailyPuzzleCurrentIndex];
        }
        else
        {
            selectedCategory = PlayerPrefs.GetString("SelectedCategory", "1");
            gridSize = PlayerPrefs.GetInt("Difficulty", 6);
            selectedImageIndex = PlayerPrefs.GetInt("SelectedImageIndex", -1);
        }

        switch (gridSize)
        {
            case 2: timeRemaining = easyTimeLimit; break;
            case 8: timeRemaining = normalTimeLimit; break;
            case 10: timeRemaining = hardTimeLimit; break;
            default: timeRemaining = easyTimeLimit; break;
        }

        StartNewGame();
    }

    void Update()
    {
        if (!isVictory)
        {
            timeRemaining -= Time.deltaTime;
            if (timeRemaining <= 0) timeRemaining = 0;
        }
        HandleTouchInput();
    }

    void StartNewGame()
    {
        if (!isDailyPuzzle && GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            Debug.Log("体力不足，无法开始拼图");
            BackToMenu();
            return;
        }

        isVictory = false;
        victoryPanel.SetActive(false);
        if (rewardText != null) rewardText.text = "";

        if (hintImage != null) { Destroy(hintImage.gameObject); hintImage = null; }
        foreach (Transform child in listContent) Destroy(child.gameObject);
        if (puzzleContent != null) { Destroy(puzzleContent.gameObject); puzzleContent = null; }
        activePieces.Clear();
        lockedCount = 0;

        // 加载图片
        if (selectedCategory == GameDataManager.UploadCategory)
        {
            List<string> uploadFiles = GameDataManager.GetUploadedImages();
            if (selectedImageIndex < 0 || selectedImageIndex >= uploadFiles.Count)
            {
                Debug.LogError("上传图片索引无效");
                return;
            }
            string fileName = uploadFiles[selectedImageIndex];
            string path = GameDataManager.GetUploadedImagePath(fileName);
            if (!File.Exists(path))
            {
                Debug.LogError("上传图片文件不存在: " + path);
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
            allSprites = AssetBundleManager.Instance.GetCategorySprites(selectedCategory);
            if (allSprites.Length == 0)
            {
                Debug.LogError("没有找到图片分类: " + selectedCategory);
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
        // 上传分类不支持收藏，禁用按钮
        if (favoriteButton != null)
            favoriteButton.interactable = (selectedCategory != GameDataManager.UploadCategory);

        UpdateFavoriteButtonColor();

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

        if (isDailyPuzzle && GameDataManager.IsDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex))
        {
            GenerateCompletedPuzzle(texture);
            return;
        }

        Sprite[] pieces = CutTexture(texture, rows, cols);
        List<PuzzlePieceData> pieceDataList = new List<PuzzlePieceData>();
        for (int i = 0; i < totalPieces; i++)
            pieceDataList.Add(new PuzzlePieceData(i, pieces[i], Random.Range(0, 4) * 90));
        Shuffle(pieceDataList);

        // 水平布局列表
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

    void GenerateCompletedPuzzle(Texture2D texture)
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

    void CreateGridOverlay()
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

        for (int i = 0; i <= cols; i++)
        {
            float x = -totalWidth / 2f + i * pieceSize;
            CreateGridLine(gridObj.transform, "VerticalLine_" + i, new Vector2(gridLineThickness, totalHeight), new Vector2(x, 0f));
        }
        for (int i = 0; i <= rows; i++)
        {
            float y = totalHeight / 2f - i * pieceSize;
            CreateGridLine(gridObj.transform, "HorizontalLine_" + i, new Vector2(totalWidth, gridLineThickness), new Vector2(0f, y));
        }
    }

    void CreateGridLine(Transform parent, string name, Vector2 size, Vector2 anchoredPos)
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

    void CreateListPiece(Sprite sprite, int index, int rotation)
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

    void OnListPieceClicked(PuzzlePiece listPiece, GameObject listObj)
    {
        GameObject newPieceObj = Instantiate(piecePrefab, puzzleContent);
        PuzzlePiece newPiece = newPieceObj.GetComponent<PuzzlePiece>();
        newPiece.Initialize(listPiece.GetComponent<Image>().sprite, listPiece.pieceIndex, listPiece.currentRotation);
        newPiece.interactable = true;

        int row = listPiece.pieceIndex / cols;
        int col = listPiece.pieceIndex % cols;
        float targetX = (col - (cols - 1) / 2f) * pieceSize;
        float targetY = ((rows - 1) / 2f - row) * pieceSize;
        newPiece.targetPosition = new Vector2(targetX, targetY);

        RectTransform rt = newPieceObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(pieceSize, pieceSize);

        float halfW = puzzleArea.rect.width / 2f - pieceSize / 2f;
        float halfH = puzzleArea.rect.height / 2f - pieceSize / 2f;
        rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));

        Button btn = newPieceObj.GetComponent<Button>();
        if (btn != null) Destroy(btn);

        newPiece.ClampPositionToPuzzleArea();
        activePieces.Add(newPiece);
        Destroy(listObj);
    }

    Sprite[] CutTexture(Texture2D texture, int rows, int cols)
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

    void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            T temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

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
                GameDataManager.SetDailyPuzzleImageCompleted(dailyPuzzleCurrentIndex, true);
                List<bool> flags = GameDataManager.GetDailyPuzzleCompletedFlags();
                int completedCount = 0;
                foreach (bool b in flags) if (b) completedCount++;

                if (completedCount >= GameDataManager.DailyPuzzleCount)
                {
                    // 每日拼图全部完成
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
                GameDataManager.ConsumeStamina(GameDataManager.PuzzleStaminaCost);

                int baseReward = 0;
                int experienceReward = 0;
                switch (gridSize)
                {
                    case 2:
                        baseReward = 5;
                        experienceReward = 3;
                        break;
                    case 8:
                        baseReward = 10;
                        experienceReward = 5;
                        break;
                    case 10:
                        baseReward = 15;
                        experienceReward = 8;
                        break;
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

    public void HideHint()
    {
        if (hintImage != null)
        {
            Destroy(hintImage.gameObject);
            hintImage = null;
        }
    }

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

        if (allSprites == null || allSprites.Length == 0) return;
        if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            ShowConfirm("体力不足，无法切换图片", null);
            return;
        }
        if (currentImageIndex == allSprites.Length - 1)
        {
            ShowConfirm("已经到最后一章，是否直接到第一张？", () =>
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
    }

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

        if (allSprites == null || allSprites.Length == 0) return;
        if (GameDataManager.Stamina < GameDataManager.PuzzleStaminaCost)
        {
            ShowConfirm("体力不足，无法切换图片", null);
            return;
        }
        if (currentImageIndex == 0)
        {
            ShowConfirm("已经到第一章，是否直接到最后一张？", () =>
            {
                selectedImageIndex = allSprites.Length - 1;
                StartNewGame();
            });
        }
        else
        {
            selectedImageIndex = currentImageIndex - 1;
            StartNewGame();
        }
    }

    void LoadDailyPuzzleImage(int index)
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

    void ShowConfirm(string message, System.Action onConfirm)
    {
        confirmText.text = message;
        confirmAction = onConfirm;
        confirmPanel.SetActive(true);
    }

    void OnConfirmYes()
    {
        confirmPanel.SetActive(false);
        confirmAction?.Invoke();
    }

    private void UpdateFavoriteButtonColor()
    {
        if (favoriteButton == null || selectedCategory == null || currentImageIndex < 0) return;

        Image buttonImage = favoriteButton.GetComponent<Image>();
        if (buttonImage == null) return;

        // 上传分类：禁用收藏并保持白色
        if (selectedCategory == GameDataManager.UploadCategory)
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

    void ToggleFavorite()
    {
        // 上传分类不支持收藏，直接返回
        if (selectedCategory == GameDataManager.UploadCategory) return;
        if (selectedCategory == null || currentImageIndex < 0) return;
        if (GameDataManager.IsFavorite(selectedCategory, currentImageIndex))
            GameDataManager.RemoveFavorite(selectedCategory, currentImageIndex);
        else
            GameDataManager.AddFavorite(selectedCategory, currentImageIndex);
        UpdateFavoriteButtonColor();
    }

    void HandleTouchInput()
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

            Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
            Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;
            float prevDistance = Vector2.Distance(touch0PrevPos, touch1PrevPos);
            float currentDistance = Vector2.Distance(touch0.position, touch1.position);

            if (prevDistance > 0.001f)
            {
                float zoomFactor = currentDistance / prevDistance;
                currentZoom = Mathf.Clamp(currentZoom * zoomFactor, minZoom, maxZoom);
            }

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

    bool IsPointOverPuzzleArea(Vector2 screenPoint)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(puzzleArea, screenPoint, null);
    }

    void ApplyContentTransform()
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

    void BackToMenu()
    {
        PlayerPrefs.SetInt("IsDailyPuzzle", 0);
        PlayerPrefs.Save();
        SceneManager.LoadScene("LevelScene");
    }
}

// 辅助数据结构
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