using System;
using System.Collections.Generic;

/// <summary>
/// 单个拼图碎片的状态快照，用于保存和恢复拼图进度。
/// </summary>
[Serializable]
public class PieceState
{
    #region 公共字段

    /// <summary>碎片在完整图中的原始索引（从左到右、从上到下）。</summary>
    public int pieceIndex;

    /// <summary>碎片在拼图区域内的 anchoredPosition.x。</summary>
    public float posX;

    /// <summary>碎片在拼图区域内的 anchoredPosition.y。</summary>
    public float posY;

    /// <summary>当前旋转角度（0/90/180/270）。</summary>
    public int rotation;

    /// <summary>是否已锁定（正确放置）。</summary>
    public bool isLocked;

    /// <summary>是否还在列表中（尚未拖入拼图区域）。</summary>
    public bool inList;

    #endregion
}

/// <summary>
/// 整个拼图进度的快照，包含分类、图片索引、难度、碎片状态等。
/// 通过 <see cref="UnityEngine.JsonUtility"/> 序列化为 JSON 后存储到磁盘。
/// </summary>
[Serializable]
public class PuzzleProgressData
{
    #region 元信息

    /// <summary>唯一标识，格式：category_imageIndex_gridSize。</summary>
    public string uniqueKey;

    /// <summary>选中的分类名。</summary>
    public string selectedCategory;

    /// <summary>当前实际使用的图片索引。</summary>
    public int currentImageIndex;

    /// <summary>难度（行列数，例如 6、8、10）。</summary>
    public int gridSize;

    /// <summary>是否为每日拼图模式。</summary>
    public bool isDailyPuzzle;

    /// <summary>每日拼图中当前图片在列表中的索引。</summary>
    public int dailyPuzzleCurrentIndex;

    /// <summary>剩余时间（秒）。</summary>
    public float timeRemaining;

    /// <summary>若为上传图片，记录文件名；否则为 null。</summary>
    public string uploadFileName;

    #endregion

    #region 碎片状态

    /// <summary>所有碎片的当前状态。</summary>
    public List<PieceState> pieces = new List<PieceState>();

    #endregion
}