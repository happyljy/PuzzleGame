using UnityEngine;
using System.Collections.Generic;

public static class GameDataManager
{
    // 分类列表（顺序需与按钮一致）
    public static string[] Categories = { "1", "2", "3" };
    // 对应分类的价格，0表示初始已解锁
    public static int[] CategoryPrices = { 0, 3, 200 };

    // 图片解锁设置
    public const int FreeImagesPerCategory = 5;   // 每个分类前5张免费
    public const int ImagePrice = 100;            // 每张图片的价格（可后续扩展为数组）

    private const string CoinsKey = "Coins";
    private const string UnlockPrefix = "Unlock_";
    private const string ImageUnlockPrefix = "ImageUnlock_";

    // 新增字段
    private const string PlayerNameKey = "PlayerName";
    private const string LevelKey = "Level";
    private const string ExperienceKey = "Experience";
    private const string FavoritesKey = "Favorites";
    private const string RewardClaimedPrefix = "RewardClaimed_";
    // 金币
    public static int Coins
    {
        get => PlayerPrefs.GetInt(CoinsKey, 0);
        private set
        {
            PlayerPrefs.SetInt(CoinsKey, value);
            PlayerPrefs.Save();
        }
    }

    public static void AddCoins(int amount)
    {
        Coins += amount;
    }

    public static bool SpendCoins(int amount)
    {
        if (Coins >= amount)
        {
            Coins -= amount;
            return true;
        }
        return false;
    }
    /// <summary>
    /// 检查该图片该难度是否已领取过金币奖励
    /// </summary>
    public static bool HasClaimedReward(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        return PlayerPrefs.GetInt(key, 0) == 1;
    }
    /// <summary>
    /// 标记该图片该难度已领取过金币奖励
    /// </summary>
    public static void SetRewardClaimed(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
    }
    // 分类解锁状态
    public static bool IsCategoryUnlocked(string category)
    {
        int index = System.Array.IndexOf(Categories, category);
        if (index >= 0 && CategoryPrices[index] == 0)
            return true;

        return PlayerPrefs.GetInt(UnlockPrefix + category, 0) == 1;
    }

    public static void UnlockCategory(string category)
    {
        PlayerPrefs.SetInt(UnlockPrefix + category, 1);
        PlayerPrefs.Save();
    }

    // 图片解锁状态
    public static bool IsImageUnlocked(string category, int imageIndex)
    {
        // 前 FreeImagesPerCategory 张免费
        if (imageIndex < FreeImagesPerCategory)
            return true;

        return PlayerPrefs.GetInt(ImageUnlockPrefix + category + "_" + imageIndex, 0) == 1;
    }

    public static void UnlockImage(string category, int imageIndex)
    {
        PlayerPrefs.SetInt(ImageUnlockPrefix + category + "_" + imageIndex, 1);
        PlayerPrefs.Save();
    }

    // 获取图片价格（暂时所有图片统一价格）
    public static int GetImagePrice(string category, int imageIndex)
    {
        return ImagePrice;
    }

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

    public static void AddExperience(int amount)
    {
        Experience += amount;
        Debug.Log($"获得经验：{amount}，当前经验：{Experience}，等级：{Level}");
        while (Experience >= GetRequiredExperience(Level))
        {
            Experience -= GetRequiredExperience(Level);
            Level++;
            Debug.Log($"升级！当前等级：{Level}，剩余经验：{Experience}");
        }
    }

    public static int GetRequiredExperience(int level)
    {
        // 1→2 需要10，2→3 需要15，3→4 需要20，依次递增5
        return 5 * (level + 1);
    }
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

    /// <summary>
    /// 仅在编辑器环境下重置所有数据（金币、解锁状态）
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