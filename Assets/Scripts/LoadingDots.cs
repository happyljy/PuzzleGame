using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 加载动画：让多个圆点围绕一个圆心均匀分布并整体旋转。
/// 每个圆点按固定间隔分布在圆周上，整体随时间匀速旋转。
/// </summary>
public class LoadingDots : MonoBehaviour
{
    #region 公共字段

    [Header("圆点引用")]
    [Tooltip("需要旋转的圆点，按顺序排列。如果不赋值，将自动获取所有子物体中的 RectTransform（不包含自身）。")]
    public RectTransform[] dots;

    [Header("旋转参数")]
    [Tooltip("旋转半径（像素）")]
    public float orbitRadius = 50f;

    [Tooltip("旋转速度（度/秒）")]
    public float orbitSpeed = 180f;

    #endregion

    #region 私有字段

    private float angle = 0f;   // 当前累计角度（度）

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        // 如果未手动赋值，则自动获取所有子物体中的 RectTransform（排除自身）
        if (dots == null || dots.Length == 0)
        {
            var all = GetComponentsInChildren<RectTransform>();
            var children = new List<RectTransform>();
            foreach (var rt in all)
            {
                if (rt != transform)    // 排除自身
                    children.Add(rt);
            }
            dots = children.ToArray();
        }

        // 如果没有圆点，则禁用脚本，避免后续除零或空引用
        if (dots.Length == 0)
        {
            Debug.LogWarning("LoadingDots: 没有找到任何圆点，脚本将被禁用。");
            enabled = false;
        }
    }

    private void Update()
    {
        // 累加角度
        angle += orbitSpeed * Time.deltaTime;

        // 每个圆点均匀分布在圆周上
        float angleStep = 360f / dots.Length;
        for (int i = 0; i < dots.Length; i++)
        {
            // 当前圆点的角度（弧度）
            float rad = (angle + i * angleStep) * Mathf.Deg2Rad;

            // 计算圆周位置
            Vector2 pos = new Vector2(
                Mathf.Cos(rad) * orbitRadius,
                Mathf.Sin(rad) * orbitRadius
            );

            dots[i].anchoredPosition = pos;
        }
    }

    #endregion
}