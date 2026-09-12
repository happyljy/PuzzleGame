using UnityEngine;

/// <summary>
/// 全局声音管理器。
///
/// 【这个脚本干什么？】
/// 统一管理游戏里的所有声音，包括：
///   1. 背景音乐（BGM）—— 循环播放
///   2. 点击音效 —— 按钮被按下时播放
///   3. 加载音效 —— 场景加载时循环播放
///   4. 拼图音效 —— 拼图碎片锁定、胜利等
///
/// 【为什么需要多个 AudioSource？】
/// AudioSource 是 Unity 里"播放声音的喇叭"。
/// 如果只用一个 AudioSource：
///   - 播背景音乐时按按钮，背景音乐会被中断
///   - 按两次按钮，第二次会顶掉第一次的音效
/// 所以给"每种用途"分配独立的 AudioSource，互不干扰。
///
/// 【为什么用 DontDestroyOnLoad？】
/// 切换场景（比如从主菜单进游戏）时，Unity 会销毁旧场景的所有物体。
/// 但音乐不应该断掉，所以要标记成"跨场景常驻"。
///
/// 【音量持久化】
/// 用户调好的音量用 PlayerPrefs 保存到本地，
/// 下次打开游戏还是上次设置的音量。
///
/// 【使用示例】
/// <code>
/// SoundManager.Instance.PlaySound(clip);        // 播放点击音效
/// SoundManager.Instance.PlayPuzzleSound(clip);  // 播放拼图音效
/// SoundManager.Instance.PlayLoadingSound(clip); // 播放加载音效（循环）
/// SoundManager.Instance.StopLoadingSound();     // 停止加载音效
/// </code>
/// </summary>
public class SoundManager : MonoBehaviour
{
    #region 单例

    /// <summary>
    /// 全局唯一实例。
    /// 通过 SoundManager.Instance.XXX() 访问，
    /// 保证整个游戏只有一个声音管理器。
    /// </summary>
    public static SoundManager Instance { get; private set; }

    #endregion

    #region 公共字段

    [Header("背景音乐")]
    [Tooltip("背景音乐音频文件，游戏启动时自动循环播放。")]
    /// <summary>
    /// 背景音乐的 AudioClip（音频片段）。
    /// 在 Unity Inspector 面板里拖一个音频文件进来。
    /// 建议用循环背景音乐，因为代码里设置了 loop = true。
    /// </summary>
    public AudioClip backgroundMusic;

    #endregion

    #region 公共属性

    /// <summary>
    /// 背景音乐是否正在播放。
    /// 用 => 箭头语法表示"只读属性"，等价于：
    ///   public bool IsBGMPlaying { get { return ...; } }
    /// 外部代码可以读 IsBGMPlaying 但无法直接改（防止误操作）。
    /// </summary>
    public bool IsBGMPlaying => bgmAudioSource != null && bgmAudioSource.isPlaying;

    /// <summary>
    /// 背景音乐音量（0~1）。
    /// 0 = 静音，0.5 = 一半音量，1 = 最大音量。
    /// 外部可以读，但只能用 SetBGMVolume() 修改。
    /// </summary>
    public float BGMVolume { get; private set; } = 0.5f;

    /// <summary>
    /// 音效音量（0~1），应用于点击音效和拼图音效。
    /// 注意：加载音效和背景音乐用各自的音量，不受这个影响。
    /// </summary>
    public float SFXVolume { get; private set; } = 1f;

    #endregion

    #region 私有字段

    // ---------- PlayerPrefs 键名 ----------
    // PlayerPrefs 是 Unity 提供的"本地键值对存储"，
    // 类似手机里的 SharedPreferences / 注册表。
    // 用常量存 key，避免拼错字符串。
    private const string BGMVolumeKey = "BGMVolume";
    private const string SFXVolumeKey = "SFXVolume";

    // ---------- 各类音效独立的 AudioSource ----------
    // 每个 AudioSource 就像一个独立的喇叭，
    // 一个喇叭在响，不影响另一个喇叭。
    private AudioSource clickAudioSource;      // 点击音效
    private AudioSource loadingAudioSource;    // 加载音效（循环）
    private AudioSource puzzleAudioSource;     // 拼图音效
    private AudioSource bgmAudioSource;        // 背景音乐

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake 在物体被创建时立即调用，早于 Start。
    /// 这里做"初始化"：单例保护、读音量、创建 AudioSource。
    /// </summary>
    private void Awake()
    {
        // ---------- 单例保护 ----------
        // 如果场景切换时不小心创建了第二个 SoundManager，
        // 就销毁新的，保证只有一个。
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 把自己设为全局实例
        Instance = this;

        // 让物体在场景切换时不被销毁
        DontDestroyOnLoad(gameObject);

        // ---------- 读取用户上次设置的音量 ----------
        // PlayerPrefs.GetFloat(key, default) 的语义：
        //   如果之前存过这个 key，返回存的值
        //   如果没存过，返回 default
        // 默认值：BGM 0.5（一半），SFX 1.0（最大）
        BGMVolume = PlayerPrefs.GetFloat(BGMVolumeKey, 0.5f);
        SFXVolume = PlayerPrefs.GetFloat(SFXVolumeKey, 1f);

        // ---------- 创建 4 个独立 AudioSource ----------
        // 用 AddComponent 动态挂到本物体上。

        // 1. 点击音效
        clickAudioSource = gameObject.AddComponent<AudioSource>();
        clickAudioSource.playOnAwake = false;   // 不自动播放，等代码调用

        // 2. 加载音效
        loadingAudioSource = gameObject.AddComponent<AudioSource>();
        loadingAudioSource.playOnAwake = false;
        loadingAudioSource.loop = true;         // 循环播放（加载期间一直响）

        // 3. 拼图音效
        puzzleAudioSource = gameObject.AddComponent<AudioSource>();
        puzzleAudioSource.playOnAwake = false;

        // 4. 背景音乐
        bgmAudioSource = gameObject.AddComponent<AudioSource>();
        bgmAudioSource.playOnAwake = false;
        bgmAudioSource.loop = true;             // 背景音乐循环
        bgmAudioSource.volume = BGMVolume;      // 应用保存的音量
    }

    /// <summary>
    /// Start 在物体第一帧启用时调用。
    /// 这里自动播放背景音乐。
    /// </summary>
    private void Start()
    {
        PlayBGM();
    }

    #endregion

    #region 背景音乐

    /// <summary>
    /// 播放背景音乐（如果尚未播放）。
    /// 已经播放中就不重复触发，避免打断。
    /// </summary>
    public void PlayBGM()
    {
        // 没设置背景音乐或 AudioSource 异常就跳过
        if (backgroundMusic == null || bgmAudioSource == null) return;

        // 只在"未播放"时才开始，避免重复调用导致重新开始播放
        if (!bgmAudioSource.isPlaying)
        {
            bgmAudioSource.clip = backgroundMusic;
            bgmAudioSource.Play();
        }
    }

    /// <summary>
    /// 停止背景音乐。
    /// </summary>
    public void StopBGM()
    {
        if (bgmAudioSource != null && bgmAudioSource.isPlaying)
            bgmAudioSource.Stop();
    }

    /// <summary>
    /// 设置背景音乐音量（同时保存到 PlayerPrefs）。
    /// 会立即应用到正在播放的音乐上。
    /// </summary>
    public void SetBGMVolume(float volume)
    {
        // Clamp01 把值限制在 0~1 之间，
        // 防止调用方传入 -0.5 或 2.0 之类的越界值。
        BGMVolume = Mathf.Clamp01(volume);

        // 应用到 AudioSource
        if (bgmAudioSource != null)
            bgmAudioSource.volume = BGMVolume;

        // 写入 PlayerPrefs 持久化
        // SetFloat 只是写到内存缓存，Save() 才会真正落盘。
        PlayerPrefs.SetFloat(BGMVolumeKey, BGMVolume);
        PlayerPrefs.Save();
    }

    #endregion

    #region 音效音量

    /// <summary>
    /// 设置音效音量（同时保存到 PlayerPrefs）。
    /// 影响点击音效和拼图音效的播放音量。
    ///
    /// 【注意】
    /// 音量不会立即应用到"正在播放"的音效上（PlayOneShot 是即时播完的），
    /// 只影响"之后"播放的音效。
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        SFXVolume = Mathf.Clamp01(volume);

        // 只保存，不需要遍历现有 AudioSource 改音量，
        // 因为每次 PlayOneShot 都会传入最新的 SFXVolume。
        PlayerPrefs.SetFloat(SFXVolumeKey, SFXVolume);
        PlayerPrefs.Save();
    }

    #endregion

    #region 音效播放

    /// <summary>
    /// 播放一次性点击音效。
    ///
    /// 【PlayOneShot vs Play 的区别】
    /// - Play()：需要先设置 clip，一个 AudioSource 同一时刻只能播一个。
    /// - PlayOneShot(clip, volume)：可以叠加播放，多个音效同时响也没问题。
    ///   而且它不会覆盖正在播放的音乐，适合"按钮音效"这种短音。
    /// </summary>
    public void PlaySound(AudioClip clip)
    {
        if (clip != null && clickAudioSource != null)
            clickAudioSource.PlayOneShot(clip, SFXVolume);
    }

    /// <summary>
    /// 播放一次性拼图音效（碎片锁定、胜利等）。
    /// 用独立的 AudioSource，避免和点击音效互相打断。
    /// </summary>
    public void PlayPuzzleSound(AudioClip clip)
    {
        if (clip != null && puzzleAudioSource != null)
            puzzleAudioSource.PlayOneShot(clip, SFXVolume);
    }

    #endregion

    #region 加载音效

    /// <summary>
    /// 开始播放加载音效（循环）。
    ///
    /// 【使用方式】
    /// MainMenuManager 在 StartGame() 时调用：
    ///     SoundManager.Instance.PlayLoadingSound(loadingSound);
    /// 然后在场景切换完成后调用：
    ///     SoundManager.Instance.StopLoadingSound();
    /// </summary>
    public void PlayLoadingSound(AudioClip clip)
    {
        if (clip != null && loadingAudioSource != null)
        {
            loadingAudioSource.clip = clip;
            loadingAudioSource.Play();
        }
    }

    /// <summary>
    /// 停止加载音效。
    /// </summary>
    public void StopLoadingSound()
    {
        if (loadingAudioSource != null && loadingAudioSource.isPlaying)
            loadingAudioSource.Stop();
    }

    #endregion
}