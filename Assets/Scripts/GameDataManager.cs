using UnityEngine;

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