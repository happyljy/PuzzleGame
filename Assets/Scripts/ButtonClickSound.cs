using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 按钮点击音效：在按钮被按下（PointerDown）时播放指定音效。
/// 音效通过全局 <see cref="SoundManager"/> 播放，不受按钮所在面板隐藏的影响。
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonClickSound : MonoBehaviour, IPointerDownHandler
{
    #region 公共字段

    [Header("音效")]
    [Tooltip("按钮按下时播放的音效，可在 Inspector 中指定。")]
    public AudioClip clickSound;

    #endregion

    #region 接口实现

    /// <summary>
    /// 按钮按下时触发，播放点击音效。
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        // 音效为空或 SoundManager 未初始化时忽略
        if (clickSound == null || SoundManager.Instance == null) return;

        // 通过全局 SoundManager 播放，避免因面板隐藏而中断
        SoundManager.Instance.PlaySound(clickSound);
    }

    #endregion
}