using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ButtonClickSound : MonoBehaviour, IPointerDownHandler
{
    public AudioClip clickSound;   // 在 Inspector 中指定音效
    private AudioSource audioSource;

    void Awake()
    {
        // 获取或添加 AudioSource
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // 设置 AudioSource 属性（可选）
        audioSource.playOnAwake = false;
        audioSource.clip = clickSound;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // 按下时播放音效
        if (clickSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
    }
}