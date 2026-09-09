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

    [Header("场景加载")]
    public string mainMenuSceneName = "LevelScene";

    // 保存字母的目标位置（Inspector 中手动设置或通过代码记录）
    private Vector2[] targetPositions;

    void Awake()
    {
        // 将所有字母的初始透明度设为 0（隐藏）
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
    }

    void Start()
    {
        // 记录目标位置
        targetPositions = new Vector2[flyingLetters.Length];
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            targetPositions[i] = flyingLetters[i].anchoredPosition;
        }

        // 设置飞入起始位置
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            Vector2 dir = flyDirections[i].normalized;
            float offsetX = Screen.width * 1.2f;   // 宽度偏移，字母会从更远的地方飞来
            float offsetY = Screen.height * 1.2f;  // 高度偏移
            Vector2 startOffset = new Vector2(dir.x * offsetX, dir.y * offsetY);
            flyingLetters[i].anchoredPosition = targetPositions[i] + startOffset;
        }

        StartCoroutine(PlayAnimation());
    }

    IEnumerator PlayAnimation()
    {
        // 先恢复所有飞入字母的透明度
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
    }

    IEnumerator GlowIn(Graphic graphic, float duration)
    {
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
}