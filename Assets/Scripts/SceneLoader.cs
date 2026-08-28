using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneLoader : MonoBehaviour
{
    public Button startButton;  // ÍÏ×§¸³Öµ

    void Start()
    {
        startButton.onClick.AddListener(LoadPuzzleScene);
    }

    void LoadPuzzleScene()
    {
        SceneManager.LoadScene("GameScene"); 
    }
}