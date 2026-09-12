using System.Collections;      // 协程（IEnumerator）相关
using UnityEngine;             // Unity 基础 API
using UnityEngine.UI;          // UI 组件（Graphic 等）
using UnityEngine.SceneManagement;  // 场景加载（SceneManager）

/// <summary>
/// 开场动画控制器。
///
/// 【这个脚本干什么？】
/// 游戏启动时，屏幕上依次飞入 "PuzzleGame" 这几个字母，
/// 飞完后再让后半段字母依次"亮起"，最后自动跳到主菜单场景。
///
/// 【动画流程】
///   1. 前 6 个字母（P、u、z、z、l、e）从屏幕外不同方向飞入
///   2. 后 4 个字母（G、a、m、e）从透明逐渐显示（"亮起"）
///   3. 音效淡出
///   4. 加载主菜单场景
///
/// 【跳过功能】
/// 用户点击屏幕可以跳过动画，直接进主菜单。
///
/// 【核心概念】
/// - 协程（Coroutine）：让动画分帧执行，不阻塞主线程
/// - 平滑插值（Lerp / SmoothStep）：让动画看起来顺畅
/// - 透明度（Alpha）：UI 元素"不透明度"的通道
/// </summary>
public class SplashAnimation : MonoBehaviour
{
    #region 字母引用

    [Header("字母引用（按顺序 PuzzleGame）")]
    [Tooltip("前六个字母：P、u、z、z、l、e（需要飞入）")]
    /// <summary>
    /// 需要"飞入"的字母数组。
    /// 顺序必须和 flyDirections 一一对应。
    /// 在 Inspector 里按顺序拖入 6 个字母的 RectTransform。
    /// </summary>
    public RectTransform[] flyingLetters;

    [Tooltip("后四个字母：G、a、m、e（需要亮起）")]
    /// <summary>
    /// 需要"亮起"的字母数组。
    /// 用 Graphic 而不是 RectTransform，因为只需要改透明度，不涉及位置。
    /// Graphic 是 Text、Image 等 UI 元素的基类。
    /// </summary>
    public Graphic[] glowingLetters;

    #endregion

    #region 飞入动画设置

    [Header("飞入动画设置")]
    [Tooltip("每个字母飞入的持续时间（秒）")]
    /// <summary>
    /// 单个字母从屏幕外飞到目标位置所用的时间。
    /// 值越大，动画越慢；越小，越快。
    /// </summary>
    public float flyDuration = 0.6f;

    [Tooltip("相邻字母开始飞入的间隔（秒）")]
    /// <summary>
    /// 上一个字母开始飞入后，隔多久下一个字母才开始飞。
    /// 这样会形成"依次飞入"的效果，而不是一起出现。
    /// </summary>
    public float flyDelay = 0.15f;

    [Tooltip("每个飞入字母的方向，用于计算屏幕外的起始位置")]
    /// <summary>
    /// 每个字母的飞入方向。
    /// (1, 0) 表示从右边飞入，(0, 1) 表示从上边飞入，
    /// (-1, 0) 表示从左边飞入，(0, -1) 表示从下边飞入。
    /// 顺序必须和 flyingLetters 数组一一对应。
    /// </summary>
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
    /// <summary>
    /// 单个字母从透明变成完全显示所用的时间。
    /// </summary>
    public float glowDuration = 0.3f;

    [Tooltip("相邻字母开始亮起的间隔（秒）")]
    /// <summary>
    /// 上一个字母开始亮起后，隔多久下一个字母才亮。
    /// 形成"依次亮起"的效果。
    /// </summary>
    public float glowDelay = 0.2f;

    #endregion

    #region 音效设置

    [Header("音效")]
    [Tooltip("字母飞入到位时播放的音效")]
    /// <summary>
    /// 字母"啪"一下到位时播放的音效。
    /// 不填也没关系，代码里会做 null 检查。
    /// </summary>
    public AudioClip flyArriveSound;

    [Tooltip("字母开始亮起时播放的音效")]
    /// <summary>
    /// 字母亮起时播放的音效。
    /// </summary>
    public AudioClip glowSound;

    /// <summary>
    /// 本脚本使用的 AudioSource。
    /// 在 Awake 里获取（或自动创建），用于播放上面的两个音效。
    /// </summary>
    private AudioSource audioSource;

    #endregion

    #region 场景加载

    [Header("场景加载")]
    [Tooltip("动画结束后要加载的主菜单场景名")]
    /// <summary>
    /// 动画结束后要跳转到的场景名。
    /// 这个名字必须和 Build Settings 里添加的场景名字完全一致。
    /// </summary>
    public string mainMenuSceneName = "LevelScene";

    #endregion

    #region 私有状态

    /// <summary>
    /// 每个飞入字母的最终目标位置（开始时的位置）。
    /// 
    /// 【为什么要存？】
    /// 我们在 InitializeFlyingLetters 里把字母移到了屏幕外，
    /// 但后续 FlyIn 动画需要知道"它原本应该在哪儿"。
    /// 所以在移动之前，先把原位置记下来。
    /// </summary>
    private Vector2[] targetPositions;

    /// <summary>
    /// 是否已经跳过动画。
    /// 用于防止用户连点屏幕时触发多次场景加载。
    /// </summary>
    private bool isSkipped = false;

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake 在物体被创建时立即调用，早于 Start。
    /// 这里准备 AudioSource 组件。
    /// </summary>
    private void Awake()
    {
        // 尝试从物体上获取已有的 AudioSource
        audioSource = GetComponent<AudioSource>();

        // 如果物体上没有 AudioSource，就动态加一个
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // 不要自动播放（我们只手动调用 PlayOneShot）
        audioSource.playOnAwake = false;
    }

    /// <summary>
    /// Start 在物体第一帧启用时调用。
    /// 这里初始化字母位置 + 启动动画协程。
    /// </summary>
    private void Start()
    {
        // 把飞入字母挪到屏幕外（并记录原位置）
        InitializeFlyingLetters();

        // 把亮起字母设置为透明（隐藏）
        InitializeGlowingLetters();

        // 启动动画协程
        StartCoroutine(PlayAnimation());
    }

    /// <summary>
    /// Update 每帧调用。
    /// 这里检测"用户是否点击屏幕"来跳过动画。
    /// </summary>
    private void Update()
    {
        // 如果还没跳过，并且用户按下了鼠标左键或触摸屏幕
        if (!isSkipped &&
            (Input.GetMouseButtonDown(0) ||                                  // 鼠标左键按下（PC / 编辑器）
             (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)))  // 触摸开始（手机）
        {
            SkipAnimation();
        }
        // ↑ GetMouseButtonDown(0)：0=左键，1=右键，2=中键
        //   TouchPhase.Began 表示"手指刚接触屏幕"的那一刻
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 记录飞入字母的最终位置，并将它们移动到屏幕外的起始位置，初始透明度设为 0。
    /// </summary>
    private void InitializeFlyingLetters()
    {
        // 创建一个和 flyingLetters 一样长的数组，装最终位置
        targetPositions = new Vector2[flyingLetters.Length];

        // 遍历每个字母
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            // ---------- 第 1 步：记录字母的"最终位置" ----------
            // anchoredPosition 表示"相对于父物体锚点的位置"
            // 这就是字母在 UI 上正常显示时的位置
            targetPositions[i] = flyingLetters[i].anchoredPosition;

            // ---------- 第 2 步：计算屏幕外的起始位置 ----------
            // 取方向向量的单位长度（normalized 会去掉长度，只保留方向）
            // 例：(3, 0) → (1, 0)，(0, -5) → (0, -1)
            Vector2 dir = flyDirections[i].normalized;

            // 屏幕外的偏移量，取屏幕尺寸的 1.2 倍，保证一定在屏幕外
            float offsetX = Screen.width * 1.2f;
            float offsetY = Screen.height * 1.2f;

            // 起始偏移 = 方向 × 屏幕尺寸
            // 例：方向 (-1, 0) → 偏移 (-Screen.width*1.2, 0)
            //     也就是"向左边移出屏幕 1.2 个屏幕宽"
            Vector2 startOffset = new Vector2(dir.x * offsetX, dir.y * offsetY);

            // 把字母挪到最终位置 + 屏幕外偏移的位置
            flyingLetters[i].anchoredPosition = targetPositions[i] + startOffset;

            // ---------- 第 3 步：把透明度设为 0（隐藏） ----------
            // 这样在动画开始前，字母虽然已经摆好位置，但看不见
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
    ///
    /// 【协程的工作原理】
    /// 方法返回 IEnumerator，里面用 yield return 分帧执行。
    /// yield return null           → 等一帧
    /// yield return new WaitForSeconds(x) → 等 x 秒
    /// yield return StartCoroutine(...)   → 等另一个协程完成
    /// </summary>
    private IEnumerator PlayAnimation()
    {
        // ========== 第 1 步：让飞入字母"虽然透明但可见" ==========
        // 因为初始化时设了 alpha=0 隐藏了它们，
        // 动画要开始飞了，得先让它们有透明度（否则看不见在飞）
        foreach (var letter in flyingLetters)
            SetAlpha(letter.GetComponent<Graphic>(), 1f);

        // ========== 第 2 步：依次飞入每个字母 ==========
        for (int i = 0; i < flyingLetters.Length; i++)
        {
            // 启动第 i 个字母的飞入协程（注意：这里用 StartCoroutine 单独启动，
            // 不会等它完成，而是立刻返回）
            StartCoroutine(FlyIn(flyingLetters[i], flyingLetters[i].anchoredPosition,
                                 targetPositions[i], flyDuration));

            // 等 flyDelay 秒后再启动下一个字母的飞入
            yield return new WaitForSeconds(flyDelay);
        }

        // ========== 第 3 步：等最后一个字母飞完 ==========
        // 循环里每个字母只等了 flyDelay 秒，
        // 但最后一个字母自己还需要 flyDuration 秒才能飞到位，
        // 所以这里要补一个 flyDuration
        yield return new WaitForSeconds(flyDuration);

        // ========== 第 4 步：依次亮起后四个字母 ==========
        for (int i = 0; i < glowingLetters.Length; i++)
        {
            // 启动亮起协程
            StartCoroutine(GlowIn(glowingLetters[i], glowDuration));

            // 等 glowDelay 秒后启动下一个
            yield return new WaitForSeconds(glowDelay);
        }

        // ========== 第 5 步：等待 + 淡出音效 ==========
        // 让画面停留一下
        yield return new WaitForSeconds(0.5f);

        // 淡出音频（音效音量逐渐降到 0）
        yield return StartCoroutine(FadeOutAudio());

        // ========== 第 6 步：加载主菜单 ==========
        // 如果用户在动画期间没点过屏幕，就自动跳转
        if (!isSkipped)
            SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>
    /// 字母飞入动画：从 startPos 平滑移动到 targetPos，结束时播放飞入音效。
    ///
    /// 【核心原理】
    /// 用一个 0~1 的进度值 t：
    ///   t = 0  → 还在起始位置
    ///   t = 1  → 已到目标位置
    /// 每一帧让 t 增加一点（elapsed / duration），
    /// 然后用 Lerp 按 t 插值出当前位置。
    /// </summary>
    private IEnumerator FlyIn(RectTransform rect, Vector2 startPos, Vector2 targetPos, float duration)
    {
        // 已经过去的时间
        float elapsed = 0f;

        // 确保字母一开始在起始位置
        rect.anchoredPosition = startPos;

        // 循环直到时间用尽
        while (elapsed < duration)
        {
            // 累加本帧消耗的时间
            elapsed += Time.deltaTime;

            // 计算进度 t（0~1）
            // Clamp01 保证 t 不会超过 1
            float t = Mathf.Clamp01(elapsed / duration);

            // SmoothStep 是一种"平滑缓动"函数：
            // 输入端 t 是线性的，输出端变成了"先慢后快再慢"的效果，
            // 让动画看起来更自然（不会突然启动、突然停止）
            t = Mathf.SmoothStep(0, 1, t);

            // 用 Lerp 按 t 插值当前位置
            // t=0 → startPos, t=1 → targetPos, t=0.5 → 中间位置
            rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);

            // 等下一帧继续
            yield return null;
        }

        // 循环结束后，确保精确落在目标位置
        rect.anchoredPosition = targetPos;

        // 播放"到位音效"
        PlaySound(flyArriveSound);
    }

    /// <summary>
    /// 字母亮起动画：从透明渐变到完全不透明，开始时播放亮起音效。
    /// </summary>
    private IEnumerator GlowIn(Graphic graphic, float duration)
    {
        // 一进入协程就播放亮起音效
        PlaySound(glowSound);

        // 确保初始是完全透明的
        SetAlpha(graphic, 0f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 用 Lerp 把透明度从 0 平滑过渡到 1
            SetAlpha(graphic, Mathf.Lerp(0f, 1f, t));

            yield return null;
        }

        // 最终透明度为 1（完全显示）
        SetAlpha(graphic, 1f);
    }

    /// <summary>
    /// 淡出音效：在指定时间内将 AudioSource 的音量降为 0，然后恢复原音量。
    ///
    /// 【为什么要恢复原音量？】
    /// 因为如果这个 AudioSource 后续还要复用，音量保持 0 就没声了。
    /// 恢复原值是比较保险的做法。
    ///
    /// 【注意】
    /// 此协程只负责淡出，不加载场景。
    /// </summary>
    private IEnumerator FadeOutAudio()
    {
        // const 表示编译期常量，局部常量放在方法内也合法
        const float fadeDuration = 0.5f;

        // 记录当前音量作为起点
        float startVolume = audioSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;

            // 音量从 startVolume 平滑降到 0
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeDuration);

            yield return null;
        }

        // 恢复音量供下次使用
        audioSource.volume = startVolume;
    }

    /// <summary>
    /// 跳过动画：停止所有协程并立即加载主菜单场景。
    /// </summary>
    private void SkipAnimation()
    {
        // 已经跳过就不再处理（防止连点触发多次加载）
        if (isSkipped) return;
        isSkipped = true;

        // 停掉本物体上所有协程（动画立即停止）
        StopAllCoroutines();

        // 加载主菜单场景
        SceneManager.LoadScene(mainMenuSceneName);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 设置 Graphic 组件的透明度（保留 RGB）。
    ///
    /// 【为什么不直接 graphic.color = new Color(r, g, b, a)？】
    /// 因为我们只想改透明度（a 通道），
    /// 不想动颜色（r,g,b 通道）—— 那些是美术在 Inspector 里设置好的。
    /// 所以：先读出来 → 改 a → 再赋值回去。
    /// </summary>
    private void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null) return;

        Color c = graphic.color;   // 读取当前颜色
        c.a = alpha;               // 只改 alpha 通道
        graphic.color = c;         // 写回
    }

    /// <summary>
    /// 播放一次性音效（若 clip 和 audioSource 均有效）。
    /// </summary>
    private void PlaySound(AudioClip clip)
    {
        // 两个前提：clip 不为空 + audioSource 不为空
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    #endregion
}