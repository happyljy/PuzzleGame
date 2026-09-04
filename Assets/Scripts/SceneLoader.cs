using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public Button startButton;
    public GameObject difficultyPanel;
    public Button easyButton;
    public Button normalButton;
    public Button hardButton;

    void Start()
    {
        // 开始按钮显示难度面板
        startButton.onClick.AddListener(ShowDifficultyPanel);

        // 难度按钮加载游戏场景并传递难度
        easyButton.onClick.AddListener(() => LoadGame(6));
        normalButton.onClick.AddListener(() => LoadGame(8));
        hardButton.onClick.AddListener(() => LoadGame(10));

        difficultyPanel.SetActive(false);
    }

    void ShowDifficultyPanel()
    {
        difficultyPanel.SetActive(true);
    }

    void LoadGame(int gridSize)
    {
        PlayerPrefs.SetInt("Difficulty", gridSize);
        PlayerPrefs.Save();
        SceneManager.LoadScene("GameScene"); // 请将场景名改为实际名称
    }
}