using System;
using System.Collections.Generic;

/// <summary>
/// 单个碎片的状态
/// </summary>
[Serializable]
public class PieceState
{
    public int pieceIndex;      // 碎片编号
    public float posX;          // 拼图区域内 anchoredPosition.x
    public float posY;          // 拼图区域内 anchoredPosition.y
    public int rotation;        // 当前旋转角度
    public bool isLocked;       // 是否已锁定
    public bool inList;         // 是否还在列表里（未拖入拼图区）
}

/// <summary>
/// 整个拼图进度
/// </summary>
[Serializable]
public class PuzzleProgressData
{
    public string uniqueKey;       // 唯一标识，格式：category_imageIndex_gridSize
    public string selectedCategory;
    public int currentImageIndex;
    public int gridSize;
    public bool isDailyPuzzle;
    public int dailyPuzzleCurrentIndex;
    public float timeRemaining;
    public string uploadFileName;
    public List<PieceState> pieces = new List<PieceState>();
}