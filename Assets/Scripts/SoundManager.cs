using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }
    public bool IsBGMPlaying => bgmAudioSource != null && bgmAudioSource.isPlaying;
    [Header("背景音乐")]
    public AudioClip backgroundMusic;

    private AudioSource clickAudioSource;
    private AudioSource loadingAudioSource;
    private AudioSource puzzleAudioSource;
    private AudioSource bgmAudioSource;

    // 音量范围 0~1
    public float BGMVolume { get; private set; } = 0.5f;
    public float SFXVolume { get; private set; } = 1f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 从 PlayerPrefs 读取已保存的音量
        BGMVolume = PlayerPrefs.GetFloat("BGMVolume", 0.5f);
        SFXVolume = PlayerPrefs.GetFloat("SFXVolume", 1f);

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

    void Start()
    {
        PlayBGM();
    }

    public void PlayBGM()
    {
        if (backgroundMusic == null || bgmAudioSource == null) return;
        if (!bgmAudioSource.isPlaying)
        {
            bgmAudioSource.clip = backgroundMusic;
            bgmAudioSource.Play();
        }
    }

    public void StopBGM()
    {
        if (bgmAudioSource != null && bgmAudioSource.isPlaying)
            bgmAudioSource.Stop();
    }

    /// <summary>
    /// 设置背景音乐音量（同时保存到 PlayerPrefs）
    /// </summary>
    public void SetBGMVolume(float volume)
    {
        BGMVolume = Mathf.Clamp01(volume);
        if (bgmAudioSource != null)
            bgmAudioSource.volume = BGMVolume;
        PlayerPrefs.SetFloat("BGMVolume", BGMVolume);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 设置音效音量（同时保存到 PlayerPrefs）
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        SFXVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("SFXVolume", SFXVolume);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 播放点击音效
    /// </summary>
    public void PlaySound(AudioClip clip)
    {
        if (clip != null && clickAudioSource != null)
            clickAudioSource.PlayOneShot(clip, SFXVolume);
    }

    /// <summary>
    /// 开始播放加载音效（循环）
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
    /// 停止加载音效
    /// </summary>
    public void StopLoadingSound()
    {
        if (loadingAudioSource != null && loadingAudioSource.isPlaying)
            loadingAudioSource.Stop();
    }
    /// <summary>
    /// 播放拼图音效
    /// </summary>
    public void PlayPuzzleSound(AudioClip clip)
    {
        if (clip != null && puzzleAudioSource != null)
            puzzleAudioSource.PlayOneShot(clip, SFXVolume);
    }
}