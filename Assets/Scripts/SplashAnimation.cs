using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 开场动画控制器：实现 "PuzzleGame" 字母飞入和亮起效果，
/// 并在动画结束后自动切换到主菜单场景。
/// 支持点击屏幕跳过动画。
/// </summary>
public class SplashAnimation : MonoBehaviour
{
    #region 字母引用

    [Header("字母引用（按顺序 PuzzleGame）")]
    [Tooltip("前六个字母：P、u、z、z、l、e（需要飞入）")]
    public RectTransform[] flyingLetters;

    [Tooltip("后四个字母：G、a、m、e（需要亮起）")]
    public Graphic[] glowingLetters;

    #endregion

    #region 飞入动画设置

    [Header("飞入动画设置")]
    [Tooltip("每个字母飞入的持续时间（秒）")]
    public float flyDuration = 0.6f;

    [Tooltip("相邻字母开始飞入的间隔（秒）")]
    public float flyDelay = 0.15f;

    [Tooltip("每个飞入字母的方向，用于计算屏幕外的起始位置")]
    public Vector2[] flyDirections = new Vector2[]
    {
        new Vector2(-1, 0),  // P 从左侧飞入
        new Vector2(1, 0),   // u 从右侧飞入
        new Vector2(0, 1),   // z1 从上方飞入
        new Vector2(0, -1),  // z2 从下方飞入
        new Vector2(-1, 0),  // l 从左侧飞入
        new Vector2(1, 0)    // e 从右侧飞入
    };

    #endregion

    #region 亮起动画设置

    [Header("亮起动画设置")]
    [Tooltip("每个字母亮起的持续时间（秒）")]
    public float glowDuration = 0.3f;

    [Tooltip("相邻字母开始亮起的间隔（秒）")]
    public float glowDelay = 0.2f;

    #endregion

    #region 音效设置

    [Header("音效")]
    [Tooltip("字母飞入到位时播放的音效")]
    public AudioClip flyArriveSound;

    [Tooltip("字母开始亮起时播放的音效")]
    public AudioClip glowSound;

    private AudioSource audioSource;   // 用于播放上述音效

    #endregion

    #region 场景加载

    [Header("场景加载")]
    [Tooltip("动画结束后要加载的主菜单场景名")]
    public string mainMenuSceneName = "LevelScene";

    #endregion

    #region 私有状态

    private Vector2[] targetPositions;  // 每个飞入字母的最终目标位置
    private bool isSkipped = false;     // 是否已跳过动画，防止重复加载

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        // 获取或添加 AudioSource，用于播放飞入和亮起音效
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void Start()
    {
        InitializeFlyingLetters();
        InitializeGlowingLetters();

        StartCoroutine(PlayAnimation());
    }

    private void Update()
    {
        // 点击屏幕（鼠标左键或触摸）跳过动画
        if (!isSkipped &&
            (Input.GetMouseButtonDown(0) ||
             (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)))
        {
            SkipAnimation();
        }
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 记录飞入字母的最终位置，并将它们移动到屏幕外的起始位置，初始透明度设为 0。
    /// </summary>
    private void InitializeFlyingLetters()
    {
        targetPositions = new Vector2[flyingLetters.Length];

        for (int i = 0; i < flyingLetters.Length; i++)
        {
            // 记录最终位置
            targetPositions[i] = flyingLetters[i].anchoredPosition;

            // 根据飞入方向计算屏幕外起始位置
            Vector2 dir = flyDirections[i].normalized;
            float offsetX = Screen.width * 1.2f;
            float offsetY = Screen.height * 1.2f;
            Vector2 startOffset = new Vector2(dir.x * offsetX, dir.y * offsetY);
            flyingLetters[i].anchoredPosition = targetPositions[i] + startOffset;

            // 初始透明度为 0
            SetAlpha(flyingLetters[i].GetComponent<Graphic>(), 0f);
        }
    }

    /// <summary>
    /// 将后四个字母的初始透明度设为 0（隐藏）。
    /// </summary>
    private void InitializeGlowingLetters()
    {
        foreach (var letter in glowingLetters)
            SetAlpha(letter, 0f);
    }

    #endregion

    #region 动画流程

    /// <summary>
    /// 主动画协程：依次执行飞入、亮起、淡出，最后加载主菜单场景。
    /// </summary>
    private IEnumerator PlayAnimation()
    {
        // 1. 恢复飞入字母的透明度（从透明变为可见，但位置仍在屏幕外）
        foreach (var letter in flyingLetters)
            SetAlpha(letter.GetComponent<Graphic>(), 1f);

        // 2. 依次飞入每个字母
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            StartCoroutine(FlyIn(flyingLetters[i], flyingLetters[i].anchoredPosition,
                                 targetPositions[i], flyDuration));
            yield return new WaitForSeconds(flyDelay);
        }

        // 等待最后一个字母飞入完成
        yield return new WaitForSeconds(flyDuration);

        // 3. 依次亮起后四个字母
        for (int i = 0; i < glowingLetters.Length; i++)
        {
            StartCoroutine(GlowIn(glowingLetters[i], glowDuration));
            yield return new WaitForSeconds(glowDelay);
        }

        // 4. 等待片刻，然后淡出音效
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(FadeOutAudio());

        // 如果未被跳过，则加载主菜单
        if (!isSkipped)
            SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>
    /// 字母飞入动画：从 startPos 平滑移动到 targetPos，结束时播放飞入音效。
    /// </summary>
    private IEnumerator FlyIn(RectTransform rect, Vector2 startPos, Vector2 targetPos, float duration)
    {
        float elapsed = 0f;
        rect.anchoredPosition = startPos;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = Mathf.SmoothStep(0, 1, t);   // 平滑缓动
            rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }

        rect.anchoredPosition = targetPos;

        // 飞入到位后播放音效
        PlaySound(flyArriveSound);
    }

    /// <summary>
    /// 字母亮起动画：从透明渐变到完全不透明，开始时播放亮起音效。
    /// </summary>
    private IEnumerator GlowIn(Graphic graphic, float duration)
    {
        // 播放亮起音效
        PlaySound(glowSound);

        // 确保初始透明度为 0
        SetAlpha(graphic, 0f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetAlpha(graphic, Mathf.Lerp(0f, 1f, t));
            yield return null;
        }

        // 最终透明度为 1
        SetAlpha(graphic, 1f);
    }

    /// <summary>
    /// 淡出音效：在指定时间内将 AudioSource 的音量降为 0，然后恢复原音量。
    /// 注意：此协程只负责淡出，不加载场景。
    /// </summary>
    private IEnumerator FadeOutAudio()
    {
        const float fadeDuration = 0.5f;
        float startVolume = audioSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeDuration);
            yield return null;
        }

        audioSource.volume = startVolume;   // 恢复音量供下次使用
    }

    /// <summary>
    /// 跳过动画：停止所有协程并立即加载主菜单场景。
    /// </summary>
    private void SkipAnimation()
    {
        if (isSkipped) return;
        isSkipped = true;

        StopAllCoroutines();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 设置 Graphic 组件的透明度（保留 RGB）。
    /// </summary>
    private void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null) return;
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    /// <summary>
    /// 播放一次性音效（若 clip 和 audioSource 均有效）。
    /// </summary>
    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    #endregion
}