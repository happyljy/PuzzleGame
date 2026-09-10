using UnityEngine;

/// <summary>
/// 全局声音管理器：统一管理背景音乐（BGM）、点击音效、加载音效、拼图音效。
/// 使用多个 AudioSource 分别处理不同类型的音频，互不干扰。
/// 通过 <see cref="DontDestroyOnLoad"/> 跨场景常驻，并提供音量持久化。
/// 
/// 使用示例：
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

    public static SoundManager Instance { get; private set; }

    #endregion

    #region 公共字段

    [Header("背景音乐")]
    [Tooltip("背景音乐音频文件，游戏启动时自动循环播放。")]
    public AudioClip backgroundMusic;

    #endregion

    #region 公共属性

    /// <summary>背景音乐是否正在播放。</summary>
    public bool IsBGMPlaying => bgmAudioSource != null && bgmAudioSource.isPlaying;

    /// <summary>背景音乐音量（0~1）。</summary>
    public float BGMVolume { get; private set; } = 0.5f;

    /// <summary>音效音量（0~1），应用于点击音效和拼图音效。</summary>
    public float SFXVolume { get; private set; } = 1f;

    #endregion

    #region 私有字段

    // PlayerPrefs 键名
    private const string BGMVolumeKey = "BGMVolume";
    private const string SFXVolumeKey = "SFXVolume";

    // 各类音效独立的 AudioSource
    private AudioSource clickAudioSource;      // 点击音效
    private AudioSource loadingAudioSource;    // 加载音效（循环）
    private AudioSource puzzleAudioSource;     // 拼图音效
    private AudioSource bgmAudioSource;        // 背景音乐

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        // 单例保护
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 从 PlayerPrefs 读取已保存的音量
        BGMVolume = PlayerPrefs.GetFloat(BGMVolumeKey, 0.5f);
        SFXVolume = PlayerPrefs.GetFloat(SFXVolumeKey, 1f);

        // 初始化各类 AudioSource
        clickAudioSource = gameObject.AddComponent<AudioSource>();
        clickAudioSource.playOnAwake = false;

        loadingAudioSource = gameObject.AddComponent<AudioSource>();
        loadingAudioSource.playOnAwake = false;
        loadingAudioSource.loop = true;

        puzzleAudioSource = gameObject.AddComponent<AudioSource>();
        puzzleAudioSource.playOnAwake = false;

        bgmAudioSource = gameObject.AddComponent<AudioSource>();
        bgmAudioSource.playOnAwake = false;
        bgmAudioSource.loop = true;
        bgmAudioSource.volume = BGMVolume;
    }

    private void Start()
    {
        // 游戏启动时自动播放背景音乐
        PlayBGM();
    }

    #endregion

    #region 背景音乐

    /// <summary>
    /// 播放背景音乐（如果尚未播放）。
    /// </summary>
    public void PlayBGM()
    {
        if (backgroundMusic == null || bgmAudioSource == null) return;

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
    /// </summary>
    public void SetBGMVolume(float volume)
    {
        BGMVolume = Mathf.Clamp01(volume);

        if (bgmAudioSource != null)
            bgmAudioSource.volume = BGMVolume;

        PlayerPrefs.SetFloat(BGMVolumeKey, BGMVolume);
        PlayerPrefs.Save();
    }

    #endregion

    #region 音效音量

    /// <summary>
    /// 设置音效音量（同时保存到 PlayerPrefs）。
    /// 影响点击音效和拼图音效的播放音量。
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        SFXVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SFXVolumeKey, SFXVolume);
        PlayerPrefs.Save();
    }

    #endregion

    #region 音效播放

    /// <summary>
    /// 播放一次性点击音效。
    /// </summary>
    public void PlaySound(AudioClip clip)
    {
        if (clip != null && clickAudioSource != null)
            clickAudioSource.PlayOneShot(clip, SFXVolume);
    }

    /// <summary>
    /// 播放一次性拼图音效（碎片锁定、胜利等）。
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