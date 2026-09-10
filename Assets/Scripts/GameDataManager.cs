using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 游戏数据管理器：负责所有持久化数据的读写，包括金币、经验、等级、收藏、
/// 体力、每日拼图、分类和图片解锁状态、上传图片、共享图片、头像等。
/// 所有数据通过 PlayerPrefs 存储，文件通过 Application.persistentDataPath 存储。
/// 该类为静态类，无需挂载到场景中。
/// </summary>
public static class GameDataManager
{
    #region 基础配置

    /// <summary>分类列表（顺序需与主菜单按钮一致）。</summary>
    public static string[] Categories = { "Kazimierz", "Kjerag", "RhodesIsland", "Ursus", "Victoria", "Yan" };

    /// <summary>分类价格，0 表示初始已解锁（当前所有分类免费）。</summary>
    public static int[] CategoryPrices = { 0, 0, 0, 0, 0, 0 };

    /// <summary>每个分类前 5 张图片免费。</summary>
    public const int FreeImagesPerCategory = 5;

    /// <summary>第 6 张及以后的图片统一价格。</summary>
    public const int ImagePrice = 100;

    /// <summary>上传图片的特殊分类标识。</summary>
    public const string UploadCategory = "Upload";

    /// <summary>共享图片的特殊分类标识。</summary>
    public const string SharedCategory = "Shared";

    #endregion

    #region PlayerPrefs 键名常量

    // 金币与解锁
    private const string CoinsKey = "Coins";
    private const string UnlockPrefix = "Unlock_";
    private const string ImageUnlockPrefix = "ImageUnlock_";

    // 玩家信息
    private const string PlayerNameKey = "PlayerName";
    private const string LevelKey = "Level";
    private const string ExperienceKey = "Experience";
    private const string FavoritesKey = "Favorites";
    private const string RewardClaimedPrefix = "RewardClaimed_";

    // 体力系统
    private const string StaminaKey = "Stamina";
    private const string LastStaminaTimeKey = "LastStaminaTime";

    // 每日拼图
    private const string DailyPuzzleDateKey = "DailyPuzzleDate";
    private const string DailyPuzzleImagesKey = "DailyPuzzleImages";
    private const string DailyPuzzleDifficultiesKey = "DailyPuzzleDifficulties";
    private const string DailyPuzzleCompletedKey = "DailyPuzzleCompleted";
    private const string DailyPuzzleCompletedFlagsKey = "DailyPuzzleCompletedFlags";

    // 上传与共享图片
    private const string UploadImagesKey = "UploadImages";
    private const string SharedImagesKey = "SharedImages";

    // 头像
    private const string AvatarIndexKey = "AvatarIndex";

    #endregion

    #region 体力系统常量

    private const int BaseStamina = 100;                       // 初始体力上限
    private const int StaminaIncreasePer5Levels = 50;          // 每 5 级增加的上限
    private const int StaminaRecoveryIntervalSeconds = 60;     // 每 60 秒恢复 1 点
    private const int StaminaRecoveryAmount = 1;               // 每次恢复量
    public const int PuzzleStaminaCost = 10;                   // 每次拼图消耗体力

    #endregion

    #region 每日拼图常量

    /// <summary>每日拼图总张数。</summary>
    public const int DailyPuzzleCount = 10;

    #endregion

    #region 体力系统

    /// <summary>
    /// 当前体力值（读取时自动根据离线时间恢复）。
    /// </summary>
    public static int Stamina
    {
        get
        {
            UpdateStaminaRecovery();
            return PlayerPrefs.GetInt(StaminaKey, MaxStamina);
        }
        private set
        {
            PlayerPrefs.SetInt(StaminaKey, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 体力上限（根据等级计算，初始 100，每 5 级 +50）。
    /// </summary>
    public static int MaxStamina
    {
        get
        {
            int levelGroup = (Level - 1) / 5; // 等级 1-4→0，5-9→1，10-14→2...
            return BaseStamina + levelGroup * StaminaIncreasePer5Levels;
        }
    }

    /// <summary>
    /// 增加体力，不会超过上限。
    /// </summary>
    public static void AddStamina(int amount)
    {
        UpdateStaminaRecovery();
        Stamina = Mathf.Min(MaxStamina, Stamina + amount);
    }

    /// <summary>
    /// 消耗体力，成功返回 true，失败返回 false。
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
    /// 根据离线时间自动恢复体力。
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

            // 只减去完整恢复周期的秒数，保留不足一次恢复的零头
            DateTime newLastTime = lastTime.AddSeconds(recoveryCount * StaminaRecoveryIntervalSeconds);
            PlayerPrefs.SetString(LastStaminaTimeKey, newLastTime.Ticks.ToString());
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 初始化体力系统（首次游戏时设置满体力）。
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

    #endregion

    #region 金币

    /// <summary>
    /// 当前金币数量。
    /// </summary>
    public static int Coins
    {
        get => PlayerPrefs.GetInt(CoinsKey, 0);
        private set
        {
            PlayerPrefs.SetInt(CoinsKey, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>增加金币。</summary>
    public static void AddCoins(int amount) => Coins += amount;

    /// <summary>消费金币，成功返回 true，余额不足返回 false。</summary>
    public static bool SpendCoins(int amount)
    {
        if (Coins >= amount)
        {
            Coins -= amount;
            return true;
        }
        return false;
    }

    #endregion

    #region 首通奖励标记

    /// <summary>
    /// 检查某张图片的某个难度是否已领取过金币奖励。
    /// </summary>
    public static bool HasClaimedReward(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        return PlayerPrefs.GetInt(key, 0) == 1;
    }

    /// <summary>
    /// 标记某张图片的某个难度已领取金币奖励。
    /// </summary>
    public static void SetRewardClaimed(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
    }

    #endregion

    #region 分类与图片解锁

    /// <summary>
    /// 判断分类是否解锁（当前所有分类免费，始终返回 true）。
    /// </summary>
    public static bool IsCategoryUnlocked(string category)
    {
        return true;
    }

    /// <summary>
    /// 解锁分类（保留方法，以备后续付费分类扩展）。
    /// </summary>
    public static void UnlockCategory(string category)
    {
        PlayerPrefs.SetInt(UnlockPrefix + category, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 判断某分类下的第 imageIndex 张图片是否解锁。
    /// </summary>
    public static bool IsImageUnlocked(string category, int imageIndex)
    {
        // 前 FreeImagesPerCategory 张免费
        if (imageIndex < FreeImagesPerCategory) return true;
        return PlayerPrefs.GetInt(ImageUnlockPrefix + category + "_" + imageIndex, 0) == 1;
    }

    /// <summary>
    /// 解锁图片。
    /// </summary>
    public static void UnlockImage(string category, int imageIndex)
    {
        PlayerPrefs.SetInt(ImageUnlockPrefix + category + "_" + imageIndex, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 获取图片价格（目前统一价格）。
    /// </summary>
    public static int GetImagePrice(string category, int imageIndex)
    {
        return ImagePrice;
    }

    #endregion

    #region 玩家信息（名字、等级、经验）

    /// <summary>玩家名字。</summary>
    public static string PlayerName
    {
        get => PlayerPrefs.GetString(PlayerNameKey, "");
        set { PlayerPrefs.SetString(PlayerNameKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>玩家等级。</summary>
    public static int Level
    {
        get => PlayerPrefs.GetInt(LevelKey, 1);
        private set { PlayerPrefs.SetInt(LevelKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>当前经验值（未达到下一级的部分）。</summary>
    public static int Experience
    {
        get => PlayerPrefs.GetInt(ExperienceKey, 0);
        private set { PlayerPrefs.SetInt(ExperienceKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// 增加经验，自动处理升级。
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
    /// 获取指定等级升级所需经验值（公式：5 × (等级 + 1)）。
    /// </summary>
    public static int GetRequiredExperience(int level)
    {
        return 5 * (level + 1);
    }

    #endregion

    #region 收藏夹

    /// <summary>
    /// 获取收藏列表（字符串列表，每个元素格式 "分类_图片索引"）。
    /// </summary>
    public static List<string> GetFavorites()
    {
        string saved = PlayerPrefs.GetString(FavoritesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>添加收藏。</summary>
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

    /// <summary>判断是否已收藏。</summary>
    public static bool IsFavorite(string category, int imageIndex)
    {
        string key = category + "_" + imageIndex;
        return GetFavorites().Contains(key);
    }

    /// <summary>移除收藏。</summary>
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

    #endregion

    #region 每日拼图

    /// <summary>
    /// 检查今日每日拼图是否已生成。
    /// </summary>
    public static bool IsDailyPuzzleGeneratedToday()
    {
        return PlayerPrefs.GetString(DailyPuzzleDateKey, "") == DateTime.UtcNow.ToString("yyyyMMdd");
    }

    /// <summary>
    /// 检查今日每日拼图是否已全部完成。
    /// </summary>
    public static bool IsDailyPuzzleCompletedToday()
    {
        return IsDailyPuzzleGeneratedToday() && PlayerPrefs.GetInt(DailyPuzzleCompletedKey, 0) == 1;
    }

    /// <summary>
    /// 生成新的每日拼图（随机选择 10 张图片，随机难度，所有分类包括未解锁）。
    /// </summary>
    public static void GenerateDailyPuzzle()
    {
        // 收集所有分类的图片
        List<string> allImages = new List<string>();
        foreach (string category in Categories)
        {
            Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites(category);
            for (int i = 0; i < sprites.Length; i++)
                allImages.Add(category + "_" + i);
        }

        // 随机抽取 10 张不重复图片，随机难度
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
    /// 获取每日拼图每张是否已完成（返回 bool 列表）。
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
    /// 设置每日拼图中某张图片的完成状态。
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
    /// 检查每日拼图中某张图片是否已完成。
    /// </summary>
    public static bool IsDailyPuzzleImageCompleted(int imageIndex)
    {
        List<bool> flags = GetDailyPuzzleCompletedFlags();
        if (imageIndex < flags.Count) return flags[imageIndex];
        return false;
    }

    /// <summary>
    /// 获取每日拼图图片列表。
    /// </summary>
    public static List<string> GetDailyPuzzleImages()
    {
        string saved = PlayerPrefs.GetString(DailyPuzzleImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>
    /// 获取每日拼图难度列表。
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
    /// 标记每日拼图全部完成。
    /// </summary>
    public static void SetDailyPuzzleCompleted()
    {
        PlayerPrefs.SetInt(DailyPuzzleCompletedKey, 1);
        PlayerPrefs.Save();
    }

    #endregion

    #region 上传图片

    /// <summary>
    /// 获取所有已上传图片的文件名列表。
    /// </summary>
    public static List<string> GetUploadedImages()
    {
        string saved = PlayerPrefs.GetString(UploadImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>
    /// 添加一个上传图片文件名。
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
    /// 删除一个上传图片（同时删除磁盘文件）。
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
    /// 获取上传图片的完整路径。
    /// </summary>
    public static string GetUploadedImagePath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, "Uploads", fileName);
    }

    #endregion

    #region 共享图片

    /// <summary>
    /// 获取所有共享图片的文件名列表。
    /// </summary>
    public static List<string> GetSharedImages()
    {
        string saved = PlayerPrefs.GetString(SharedImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>
    /// 添加一个共享图片文件名。
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
    /// 获取共享图片的完整路径。
    /// </summary>
    public static string GetSharedImagePath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, "Shared", fileName);
    }

    #endregion

    #region 头像

    /// <summary>
    /// 获取当前头像索引（从 0 开始）。
    /// </summary>
    public static int GetAvatarIndex()
    {
        return PlayerPrefs.GetInt(AvatarIndexKey, 0);
    }

    /// <summary>
    /// 设置当前头像索引。
    /// </summary>
    public static void SetAvatarIndex(int index)
    {
        PlayerPrefs.SetInt(AvatarIndexKey, index);
        PlayerPrefs.Save();
    }

    #endregion

    #region 编辑器工具

    /// <summary>
    /// 仅在编辑器环境下重置所有数据（金币、解锁状态等）。
    /// </summary>
    public static void ResetForEditor()
    {
#if UNITY_EDITOR
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("Editor: PlayerPrefs 已重置");
#endif
    }

    #endregion
}