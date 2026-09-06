using UnityEngine;

public static class GameDataManager
{
    // 分类列表（顺序需与按钮一致）
    public static string[] Categories = { "1", "2", "3" };
    // 对应分类的价格，0表示初始已解锁
    public static int[] CategoryPrices = { 0, 3, 200 };

    private const string CoinsKey = "Coins";
    private const string UnlockPrefix = "Unlock_";

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
        // 如果价格是0，默认解锁
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