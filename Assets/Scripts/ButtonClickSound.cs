using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ButtonClickSound : MonoBehaviour, IPointerDownHandler
{
    public AudioClip clickSound;   // 在 Inspector 中指定音效

    public void OnPointerDown(PointerEventData eventData)
    {
        if (clickSound != null && SoundManager.Instance != null)
        {
            // 使用全局 SoundManager 播放，不受面板隐藏影响
            SoundManager.Instance.PlaySound(clickSound);
        }
    }
}