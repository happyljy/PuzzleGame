using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 加载动画：让多个圆点围绕一个圆心均匀分布并整体旋转。
/// 
/// 【效果说明】
/// 想象一个钟表，圆心是轴，圆点是钟表上的小圆珠。
/// - 所有圆点均匀分布在圆周上（比如 4 个点就间隔 90°）
/// - 整体随时间匀速转动，形成"转圈圈"的加载动画效果
/// 
/// 【使用方法】
/// 1. 在 Unity 里创建一个空物体（比如叫 LoadingSpinner）
/// 2. 在这个空物体下创建若干 Image 作为"圆点"，摆放成圆形
/// 3. 把 LoadingDots 脚本挂到空物体上
/// 4. 如果不想手动拖引用，脚本会按顺序自动抓取所有子物体
/// </summary>
public class LoadingDots : MonoBehaviour
{
    #region 公共字段（在 Unity Inspector 面板上可以调整）

    [Header("圆点引用")]
    [Tooltip("需要旋转的圆点，按顺序排列。如果不赋值，将自动获取所有子物体中的 RectTransform（不包含自身）。")]
    /// <summary>
    /// 装所有"圆点"的数组。
    /// - 每个元素都是一个 RectTransform（UI 元素的"位置/大小"控制器）
    /// - 可以在 Inspector 里手动拖进去，也可以留空让脚本自动抓取子物体
    /// - 顺序很重要：数组里排第 1 位的圆点，转动时会被放在最靠前的角度
    /// </summary>
    public RectTransform[] dots;

    [Header("旋转参数")]
    [Tooltip("旋转半径（像素）")]
    /// <summary>
    /// 圆点到圆心的距离（单位：像素）。
    /// 值越大，圆点转的圈越大；值越小，圈越紧凑。
    /// 例：50 表示圆点距离中心 50 像素。
    /// </summary>
    public float orbitRadius = 50f;

    [Tooltip("旋转速度（度/秒）")]
    /// <summary>
    /// 每秒转过的角度。
    /// - 180 表示每秒钟转半圈（速度适中）
    /// - 360 表示每秒钟转一圈（较快）
    /// - -180 表示反向转（逆时针）
    /// </summary>
    public float orbitSpeed = 180f;

    #endregion

    #region 私有字段（只在脚本内部使用，Inspector 里看不到）

    /// <summary>
    /// 当前累计的角度（单位：度）。
    /// 每一帧都会累加 orbitSpeed × 每帧耗时，让角度不断增加，圆点就转起来了。
    /// 累加后不会归零（比如累加到 370° 也继续往上涨），因为 cos/sin 是周期函数，
    /// 370° 和 10° 的位置是一样的，无需手动循环清零。
    /// </summary>
    private float angle = 0f;

    #endregion

    #region Unity 生命周期（Unity 自动调用的方法）

    /// <summary>
    /// Start 在游戏对象第一次启用时执行一次。
    /// 这里用来做"初始化"：自动找圆点、检查是否有效。
    /// </summary>
    private void Start()
    {
        // 如果用户在 Inspector 里没拖任何圆点（数组为空），就自动找
        if (dots == null || dots.Length == 0)
        {
            // GetComponentsInChildren<RectTransform>() 会拿到
            // "自己 + 所有子物体 + 所有孙物体"上所有的 RectTransform
            var all = GetComponentsInChildren<RectTransform>();

            // 用一个临时列表装"排除自己以后的子物体"
            var children = new List<RectTransform>();

            // 遍历刚拿到的所有 RectTransform
            foreach (var rt in all)
            {
                // 只保留"不是自己"的那些（否则中心点也会被当成圆点转）
                if (rt != transform)    // transform 就是脚本所在物体自身
                    children.Add(rt);
            }

            // 把列表转成数组，赋给 dots 字段
            dots = children.ToArray();
        }

        // 如果找完还是空的（说明这个物体下没有子物体），就禁用脚本
        if (dots.Length == 0)
        {
            // 输出警告方便开发者排查
            Debug.LogWarning("LoadingDots: 没有找到任何圆点，脚本将被禁用。");
            // 禁用脚本，避免后面 Update 里 dots.Length = 0 导致除零错误
            enabled = false;
        }
    }

    /// <summary>
    /// Update 每帧调用一次（通常一秒 60 次）。
    /// 这里做"动画"：更新累计角度，重新计算每个圆点的位置。
    /// </summary>
    private void Update()
    {
        // ---------- 第一步：累加角度 ----------
        // Time.deltaTime 是"上一帧到这一帧经过了多少秒"
        // 例如 60 帧时，每帧约 0.0167 秒
        // orbitSpeed × deltaTime = 这一帧该转多少度
        angle += orbitSpeed * Time.deltaTime;

        // ---------- 第二步：计算每个圆点的角度间隔 ----------
        // 例：如果有 4 个圆点，360°/4 = 90°，说明相邻两个点相隔 90°
        float angleStep = 360f / dots.Length;

        // ---------- 第三步：逐个计算并设置每个圆点的位置 ----------
        for (int i = 0; i < dots.Length; i++)
        {
            // 计算第 i 个圆点当前的绝对角度（单位：度）
            // angle 是整体旋转角度，i × angleStep 是它相对于第一个点的偏移
            // 最后 × Mathf.Deg2Rad 把"度"换算成"弧度"（数学函数用弧度）
            float rad = (angle + i * angleStep) * Mathf.Deg2Rad;

            // 用三角函数计算圆周上的坐标：
            //   x = 半径 × cos(角度)
            //   y = 半径 × sin(角度)
            // 例：角度 0°  → (半径, 0)
            //     角度 90° → (0, 半径)
            //     角度 180°→ (-半径, 0)
            // 这样就能得到一个围着中心转的坐标
            Vector2 pos = new Vector2(
                Mathf.Cos(rad) * orbitRadius,
                Mathf.Sin(rad) * orbitRadius
            );

            // 把计算出的坐标应用到圆点的 UI 位置上
            // anchoredPosition 表示"相对于父物体锚点的位置"
            dots[i].anchoredPosition = pos;
        }
    }

    #endregion
}