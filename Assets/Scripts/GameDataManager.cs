using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 游戏数据管理器：负责所有持久化数据的读写，包括金币、经验、等级、收藏、
/// 体力、每日拼图、分类和图片解锁状态、上传图片、共享图片、头像等。
/// 所有数据通过 PlayerPrefs 存储，文件通过 Application.persistentDataPath 存储。
/// </summary>
public static class GameDataManager
{
    // ==================== 基础配置 ====================

    // 分类列表（顺序需与主菜单按钮一致）
    public static string[] Categories = { "1", "2", "3" };
    // 对应分类的价格，0 表示初始已解锁（当前所有分类免费）
    public static int[] CategoryPrices = { 0, 0, 0 };

    // 图片解锁设置
    public const int FreeImagesPerCategory = 5;   // 每个分类前5张图片免费
    public const int ImagePrice = 100;            // 第6张及以后的图片价格（统一价格）

    // 特殊分类标识
    public const string UploadCategory = "Upload";   // 上传图片的特殊分类
    public const string SharedCategory = "Shared";   // 共享图片的特殊分类

    // ==================== PlayerPrefs 键名常量 ====================

    private const string CoinsKey = "Coins";                          // 金币
    private const string UnlockPrefix = "Unlock_";                    // 分类解锁前缀
    private const string ImageUnlockPrefix = "ImageUnlock_";          // 图片解锁前缀
    private const string PlayerNameKey = "PlayerName";                // 玩家名字
    private const string LevelKey = "Level";                          // 等级
    private const string ExperienceKey = "Experience";                // 经验值
    private const string FavoritesKey = "Favorites";                  // 收藏列表
    private const string RewardClaimedPrefix = "RewardClaimed_";      // 首通奖励标记前缀

    // 体力系统
    private const string StaminaKey = "Stamina";                       // 当前体力值
    private const string LastStaminaTimeKey = "LastStaminaTime";       // 上次体力恢复时间戳
    private const int BaseStamina = 100;                               // 初始体力上限
    private const int StaminaIncreasePer5Levels = 50;                  // 每5级增加的上限
    private const int StaminaRecoveryIntervalSeconds = 60;             // 每60秒恢复1点体力
    private const int StaminaRecoveryAmount = 1;                       // 每次恢复量
    public const int PuzzleStaminaCost = 10;                           // 每次拼图消耗体力

    // 每日拼图
    public const int DailyPuzzleCount = 10;                            // 每日拼图总张数
    private const string DailyPuzzleDateKey = "DailyPuzzleDate";       // 生成日期（yyyyMMdd）
    private const string DailyPuzzleImagesKey = "DailyPuzzleImages";   // 图片列表（分类_索引）
    private const string DailyPuzzleDifficultiesKey = "DailyPuzzleDifficulties"; // 难度列表
    private const string DailyPuzzleCompletedKey = "DailyPuzzleCompleted"; // 是否完成全部
    private const string DailyPuzzleCompletedFlagsKey = "DailyPuzzleCompletedFlags"; // 每张完成标志（新）

    // 上传图片
    private const string UploadImagesKey = "UploadImages";             // 上传图片文件名列表

    // 共享图片
    private const string SharedImagesKey = "SharedImages";             // 共享图片文件名列表

    // 头像
    private const string AvatarIndexKey = "AvatarIndex";               // 当前头像索引

    // ==================== 体力系统 ====================

    /// <summary>
    /// 当前体力值（读取时自动根据离线时间恢复）
    /// </summary>
    public static int Stamina
    {
        get
        {
            UpdateStaminaRecovery(); // 先更新离线恢复
            return PlayerPrefs.GetInt(StaminaKey, MaxStamina);
        }
        private set
        {
            PlayerPrefs.SetInt(StaminaKey, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 体力上限（根据等级计算，初始100，每5级+50）
    /// </summary>
    public static int MaxStamina
    {
        get
        {
            int levelGroup = (Level - 1) / 5; // 等级1-4→0，5-9→1，10-14→2...
            return BaseStamina + levelGroup * StaminaIncreasePer5Levels;
        }
    }

    /// <summary>
    /// 增加体力，不会超过上限
    /// </summary>
    public static void AddStamina(int amount)
    {
        UpdateStaminaRecovery();
        Stamina = Mathf.Min(MaxStamina, Stamina + amount);
    }

    /// <summary>
    /// 消耗体力，成功返回 true，失败返回 false
    /// </summary>
    public static bool ConsumeStamina(int amount)
    {
        UpdateStaminaRecovery();
        if (Stamina >= amount)
        {
            Stamina -= amount;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 根据离线时间自动恢复体力
    /// </summary>
    private static void UpdateStaminaRecovery()
    {
        long lastTimeTicks = long.Parse(PlayerPrefs.GetString(LastStaminaTimeKey, DateTime.UtcNow.Ticks.ToString()));
        DateTime lastTime = new DateTime(lastTimeTicks);
        TimeSpan elapsed = DateTime.UtcNow - lastTime;

        int recoveryCount = (int)(elapsed.TotalSeconds / StaminaRecoveryIntervalSeconds);
        if (recoveryCount > 0)
        {
            int currentStamina = PlayerPrefs.GetInt(StaminaKey, MaxStamina);
            int newStamina = Mathf.Min(MaxStamina, currentStamina + recoveryCount * StaminaRecoveryAmount);
            PlayerPrefs.SetInt(StaminaKey, newStamina);
            // 更新最后恢复时间（只减去完整恢复周期的秒数，保留零头）
            DateTime newLastTime = lastTime.AddSeconds(recoveryCount * StaminaRecoveryIntervalSeconds);
            PlayerPrefs.SetString(LastStaminaTimeKey, newLastTime.Ticks.ToString());
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 初始化体力系统（首次游戏时设置满体力）
    /// </summary>
    public static void InitStaminaSystem()
    {
        if (!PlayerPrefs.HasKey(StaminaKey))
        {
            PlayerPrefs.SetInt(StaminaKey, MaxStamina);
            PlayerPrefs.SetString(LastStaminaTimeKey, DateTime.UtcNow.Ticks.ToString());
            PlayerPrefs.Save();
        }
    }

    // ==================== 金币 ====================

    public static int Coins
    {
        get => PlayerPrefs.GetInt(CoinsKey, 0);
        private set
        {
            PlayerPrefs.SetInt(CoinsKey, value);
            PlayerPrefs.Save();
        }
    }

    public static void AddCoins(int amount) => Coins += amount;

    public static bool SpendCoins(int amount)
    {
        if (Coins >= amount)
        {
            Coins -= amount;
            return true;
        }
        return false;
    }

    // ==================== 首通奖励标记 ====================

    /// <summary>
    /// 检查某张图片的某个难度是否已领取过金币奖励
    /// </summary>
    public static bool HasClaimedReward(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        return PlayerPrefs.GetInt(key, 0) == 1;
    }

    /// <summary>
    /// 标记某张图片的某个难度已领取金币奖励
    /// </summary>
    public static void SetRewardClaimed(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
    }

    // ==================== 分类与图片解锁 ====================

    /// <summary>
    /// 判断分类是否解锁（当前所有分类都解锁，直接返回 true）
    /// </summary>
    public static bool IsCategoryUnlocked(string category)
    {
        // 所有分类免费，始终解锁
        return true;
    }

    /// <summary>
    /// 解锁分类（保留方法，以备后续需要）
    /// </summary>
    public static void UnlockCategory(string category)
    {
        PlayerPrefs.SetInt(UnlockPrefix + category, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 判断某分类下的第 imageIndex 张图片是否解锁
    /// </summary>
    public static bool IsImageUnlocked(string category, int imageIndex)
    {
        // 前 FreeImagesPerCategory 张免费
        if (imageIndex < FreeImagesPerCategory)
            return true;
        return PlayerPrefs.GetInt(ImageUnlockPrefix + category + "_" + imageIndex, 0) == 1;
    }

    /// <summary>
    /// 解锁图片
    /// </summary>
    public static void UnlockImage(string category, int imageIndex)
    {
        PlayerPrefs.SetInt(ImageUnlockPrefix + category + "_" + imageIndex, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 获取图片价格（目前统一价格）
    /// </summary>
    public static int GetImagePrice(string category, int imageIndex)
    {
        return ImagePrice;
    }

    // ==================== 玩家信息（名字、等级、经验） ====================

    public static string PlayerName
    {
        get => PlayerPrefs.GetString(PlayerNameKey, "");
        set { PlayerPrefs.SetString(PlayerNameKey, value); PlayerPrefs.Save(); }
    }

    public static int Level
    {
        get => PlayerPrefs.GetInt(LevelKey, 1);
        private set { PlayerPrefs.SetInt(LevelKey, value); PlayerPrefs.Save(); }
    }

    public static int Experience
    {
        get => PlayerPrefs.GetInt(ExperienceKey, 0);
        private set { PlayerPrefs.SetInt(ExperienceKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// 增加经验，自动处理升级
    /// </summary>
    public static void AddExperience(int amount)
    {
        Experience += amount;
        while (Experience >= GetRequiredExperience(Level))
        {
            Experience -= GetRequiredExperience(Level);
            Level++;
        }
    }

    /// <summary>
    /// 获取指定等级升级所需经验值（公式：5 * (等级+1)）
    /// </summary>
    public static int GetRequiredExperience(int level)
    {
        return 5 * (level + 1);
    }

    // ==================== 收藏夹 ====================

    /// <summary>
    /// 获取收藏列表（字符串列表，每个元素格式 "分类_图片索引"）
    /// </summary>
    public static List<string> GetFavorites()
    {
        string saved = PlayerPrefs.GetString(FavoritesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    public static void AddFavorite(string category, int imageIndex)
    {
        string key = category + "_" + imageIndex;
        List<string> favorites = GetFavorites();
        if (!favorites.Contains(key))
        {
            favorites.Add(key);
            PlayerPrefs.SetString(FavoritesKey, string.Join(",", favorites));
            PlayerPrefs.Save();
        }
    }

    public static bool IsFavorite(string category, int imageIndex)
    {
        string key = category + "_" + imageIndex;
        return GetFavorites().Contains(key);
    }

    public static void RemoveFavorite(string category, int imageIndex)
    {
        string key = category + "_" + imageIndex;
        List<string> favorites = GetFavorites();
        if (favorites.Remove(key))
        {
            PlayerPrefs.SetString(FavoritesKey, string.Join(",", favorites));
            PlayerPrefs.Save();
        }
    }

    // ==================== 每日拼图 ====================

    /// <summary>
    /// 检查今日每日拼图是否已生成
    /// </summary>
    public static bool IsDailyPuzzleGeneratedToday()
    {
        return PlayerPrefs.GetString(DailyPuzzleDateKey, "") == DateTime.UtcNow.ToString("yyyyMMdd");
    }

    /// <summary>
    /// 检查今日每日拼图是否已全部完成
    /// </summary>
    public static bool IsDailyPuzzleCompletedToday()
    {
        return IsDailyPuzzleGeneratedToday() && PlayerPrefs.GetInt(DailyPuzzleCompletedKey, 0) == 1;
    }

    /// <summary>
    /// 生成新的每日拼图（随机选择10张图片，随机难度，所有分类包括未解锁）
    /// </summary>
    public static void GenerateDailyPuzzle()
    {
        List<string> allImages = new List<string>();
        foreach (string category in Categories)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>("Art/" + category);
            System.Array.Sort(sprites, (a, b) => string.Compare(a.name, b.name));
            for (int i = 0; i < sprites.Length; i++)
                allImages.Add(category + "_" + i);
        }

        List<int> indices = new List<int>();
        for (int i = 0; i < allImages.Count; i++) indices.Add(i);
        List<string> selectedImages = new List<string>();
        List<int> selectedDifficulties = new List<int>();
        int[] difficulties = { 2, 8, 10 }; // 测试用，正式可改为 { 6, 8, 10 }

        for (int i = 0; i < DailyPuzzleCount; i++)
        {
            if (indices.Count == 0) break;
            int randIdx = UnityEngine.Random.Range(0, indices.Count);
            int imageIdx = indices[randIdx];
            indices.RemoveAt(randIdx);
            selectedImages.Add(allImages[imageIdx]);
            selectedDifficulties.Add(difficulties[UnityEngine.Random.Range(0, difficulties.Length)]);
        }

        PlayerPrefs.SetString(DailyPuzzleDateKey, DateTime.UtcNow.ToString("yyyyMMdd"));
        PlayerPrefs.SetString(DailyPuzzleImagesKey, string.Join(",", selectedImages));
        PlayerPrefs.SetString(DailyPuzzleDifficultiesKey, string.Join(",", selectedDifficulties.ConvertAll(x => x.ToString())));
        PlayerPrefs.SetInt(DailyPuzzleCompletedKey, 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 获取每日拼图每张是否已完成（返回 bool 列表）
    /// </summary>
    public static List<bool> GetDailyPuzzleCompletedFlags()
    {
        string saved = PlayerPrefs.GetString(DailyPuzzleCompletedFlagsKey, "");
        List<bool> flags = new List<bool>();
        if (string.IsNullOrEmpty(saved)) return flags;
        foreach (var c in saved.Split(','))
        {
            flags.Add(c == "1");
        }
        return flags;
    }

    /// <summary>
    /// 设置每日拼图中某张图片的完成状态
    /// </summary>
    public static void SetDailyPuzzleImageCompleted(int imageIndex, bool completed)
    {
        List<bool> flags = GetDailyPuzzleCompletedFlags();
        while (flags.Count <= imageIndex) flags.Add(false);
        flags[imageIndex] = completed;
        PlayerPrefs.SetString(DailyPuzzleCompletedFlagsKey, string.Join(",", flags.ConvertAll(x => x ? "1" : "0")));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 检查每日拼图中某张图片是否已完成
    /// </summary>
    public static bool IsDailyPuzzleImageCompleted(int imageIndex)
    {
        List<bool> flags = GetDailyPuzzleCompletedFlags();
        if (imageIndex < flags.Count) return flags[imageIndex];
        return false;
    }

    /// <summary>
    /// 获取每日拼图图片列表
    /// </summary>
    public static List<string> GetDailyPuzzleImages()
    {
        string saved = PlayerPrefs.GetString(DailyPuzzleImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>
    /// 获取每日拼图难度列表
    /// </summary>
    public static List<int> GetDailyPuzzleDifficulties()
    {
        string saved = PlayerPrefs.GetString(DailyPuzzleDifficultiesKey, "");
        List<int> result = new List<int>();
        foreach (var s in saved.Split(','))
        {
            int v;
            if (int.TryParse(s, out v)) result.Add(v);
        }
        return result;
    }

    /// <summary>
    /// 标记每日拼图全部完成
    /// </summary>
    public static void SetDailyPuzzleCompleted()
    {
        PlayerPrefs.SetInt(DailyPuzzleCompletedKey, 1);
        PlayerPrefs.Save();
    }

    // ==================== 上传图片 ====================

    /// <summary>
    /// 获取所有已上传图片的文件名列表
    /// </summary>
    public static List<string> GetUploadedImages()
    {
        string saved = PlayerPrefs.GetString(UploadImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>
    /// 添加一个上传图片文件名
    /// </summary>
    public static void AddUploadedImage(string fileName)
    {
        List<string> list = GetUploadedImages();
        if (!list.Contains(fileName))
        {
            list.Add(fileName);
            PlayerPrefs.SetString(UploadImagesKey, string.Join(",", list));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 删除一个上传图片（同时删除文件）
    /// </summary>
    public static void RemoveUploadedImage(string fileName)
    {
        List<string> list = GetUploadedImages();
        if (list.Remove(fileName))
        {
            PlayerPrefs.SetString(UploadImagesKey, string.Join(",", list));
            PlayerPrefs.Save();

            string filePath = GetUploadedImagePath(fileName);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    /// <summary>
    /// 获取上传图片的完整路径
    /// </summary>
    public static string GetUploadedImagePath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, "Uploads", fileName);
    }

    // ==================== 共享图片 ====================

    /// <summary>
    /// 获取所有共享图片的文件名列表
    /// </summary>
    public static List<string> GetSharedImages()
    {
        string saved = PlayerPrefs.GetString(SharedImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>
    /// 添加一个共享图片文件名
    /// </summary>
    public static void AddSharedImage(string fileName)
    {
        List<string> list = GetSharedImages();
        if (!list.Contains(fileName))
        {
            list.Add(fileName);
            PlayerPrefs.SetString(SharedImagesKey, string.Join(",", list));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 获取共享图片的完整路径
    /// </summary>
    public static string GetSharedImagePath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, "Shared", fileName);
    }

    // ==================== 头像 ====================

    /// <summary>
    /// 获取当前头像索引（从0开始）
    /// </summary>
    public static int GetAvatarIndex()
    {
        return PlayerPrefs.GetInt(AvatarIndexKey, 0);
    }

    /// <summary>
    /// 设置当前头像索引
    /// </summary>
    public static void SetAvatarIndex(int index)
    {
        PlayerPrefs.SetInt(AvatarIndexKey, index);
        PlayerPrefs.Save();
    }

    // ==================== 编辑器工具 ====================

    /// <summary>
    /// 仅在编辑器环境下重置所有数据（金币、解锁状态等）
    /// </summary>
    public static void ResetForEditor()
    {
#if UNITY_EDITOR
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("Editor: PlayerPrefs 已重置");
#endif
    }
}