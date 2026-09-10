using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class SplashAnimation : MonoBehaviour
{
    [Header("字母引用（按顺序 PuzzleGame）")]
    public RectTransform[] flyingLetters;      // 前六个字母
    public Graphic[] glowingLetters;           // 后四个字母

    [Header("飞入动画设置")]
    public float flyDuration = 0.6f;
    public float flyDelay = 0.15f;
    public Vector2[] flyDirections = new Vector2[]
    {
        new Vector2(-1, 0),  // P 左
        new Vector2(1, 0),   // u 右
        new Vector2(0, 1),   // z1 上
        new Vector2(0, -1),  // z2 下
        new Vector2(-1, 0),  // l 左
        new Vector2(1, 0)    // e 右
    };

    [Header("亮起动画设置")]
    public float glowDuration = 0.3f;
    public float glowDelay = 0.2f;

    [Header("音效")]
    public AudioClip flyArriveSound;    // 字母飞入到位时播放
    public AudioClip glowSound;         // 字母亮起时播放
    private AudioSource audioSource;

    [Header("场景加载")]
    public string mainMenuSceneName = "LevelScene";

    private Vector2[] targetPositions;

    void Awake()
    {
        // 添加 AudioSource 用于播放音效
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    void Start()
    {
        // 记录目标位置
        targetPositions = new Vector2[flyingLetters.Length];
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            targetPositions[i] = flyingLetters[i].anchoredPosition;
        }

        // 设置飞入起始位置（屏幕外）
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            Vector2 dir = flyDirections[i].normalized;
            float offsetX = Screen.width * 1.2f;
            float offsetY = Screen.height * 1.2f;
            Vector2 startOffset = new Vector2(dir.x * offsetX, dir.y * offsetY);
            flyingLetters[i].anchoredPosition = targetPositions[i] + startOffset;
        }

        // 初始透明
        foreach (var letter in flyingLetters)
        {
            Graphic graphic = letter.GetComponent<Graphic>();
            if (graphic != null)
            {
                Color c = graphic.color;
                c.a = 0f;
                graphic.color = c;
            }
        }
        foreach (var letter in glowingLetters)
        {
            Color c = letter.color;
            c.a = 0f;
            letter.color = c;
        }

        StartCoroutine(PlayAnimation());
    }

    IEnumerator PlayAnimation()
    {
        // 恢复飞入字母透明度
        foreach (var letter in flyingLetters)
        {
            Graphic graphic = letter.GetComponent<Graphic>();
            if (graphic != null)
            {
                Color c = graphic.color;
                c.a = 1f;
                graphic.color = c;
            }
        }

        // 飞入
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            StartCoroutine(FlyIn(flyingLetters[i], flyingLetters[i].anchoredPosition, targetPositions[i], flyDuration));
            yield return new WaitForSeconds(flyDelay);
        }
        yield return new WaitForSeconds(flyDuration);

        // 亮起
        for (int i = 0; i < glowingLetters.Length; i++)
        {
            StartCoroutine(GlowIn(glowingLetters[i], glowDuration));
            yield return new WaitForSeconds(glowDelay);
        }

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(FadeOutAndLoad());
        SceneManager.LoadScene(mainMenuSceneName);
    }

    IEnumerator FlyIn(RectTransform rect, Vector2 startPos, Vector2 targetPos, float duration)
    {
        float elapsed = 0f;
        rect.anchoredPosition = startPos;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = Mathf.SmoothStep(0, 1, t);
            rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }
        rect.anchoredPosition = targetPos;

        // 播放飞入到位音效
        if (flyArriveSound != null && audioSource != null)
            audioSource.PlayOneShot(flyArriveSound);
    }

    IEnumerator GlowIn(Graphic graphic, float duration)
    {
        // 播放亮起音效
        if (glowSound != null && audioSource != null)
            audioSource.PlayOneShot(glowSound);

        Color startColor = graphic.color;
        startColor.a = 0f;
        graphic.color = startColor;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Color c = graphic.color;
            c.a = Mathf.Lerp(0f, 1f, t);
            graphic.color = c;
            yield return null;
        }
        Color finalColor = graphic.color;
        finalColor.a = 1f;
        graphic.color = finalColor;
    }

    void Update()
    {
        // 点击跳过
        if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
        {
            StopAllCoroutines();
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
    IEnumerator FadeOutAndLoad()
    {
        float fadeDuration = 0.5f;
        float startVolume = audioSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeDuration);
            yield return null;
        }

        audioSource.volume = startVolume; // 恢复音量供下次使用
        SceneManager.LoadScene(mainMenuSceneName);
    }
}