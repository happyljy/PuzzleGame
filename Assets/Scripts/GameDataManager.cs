using System;                      // DateTime、TimeSpan、Serializable 等
using System.Collections.Generic;  // List<T>
using System.IO;                   // File、Path
using UnityEngine;                 // PlayerPrefs、Mathf、Debug、Application

/// <summary>
/// 游戏数据管理器（静态类，不需要挂到场景物体上）。
///
/// 【这个类干什么？】
/// 整个游戏所有"持久化数据"的统一管家：
///   - 金币、经验、等级、玩家名字、头像
///   - 体力（含离线自动恢复）
///   - 收藏夹
///   - 每日拼图
///   - 分类/图片解锁状态
///   - 上传图片、共享图片的文件列表
///   - 拼图进度（未完成的拼图存档）
///
/// 【数据存哪里？】
/// - 小数据（数字、短字符串）：PlayerPrefs
///   Android 路径：/data/data/<包名>/shared_prefs/
///   Windows 路径：注册表 HKCU\Software\<公司名>\<产品名>\
/// - 大文件（图片）：Application.persistentDataPath 下的 Uploads / Shared 目录
///
/// 【为什么用静态类？】
/// 全局唯一、无需实例化。任何代码直接 GameDataManager.Coins 就能访问，
/// 不用 GetComponent 或 FindObjectOfType。
/// </summary>
public static class GameDataManager
{
    #region 基础配置

    /// <summary>
    /// 所有图片分类的名字。
    /// 顺序必须与主菜单里按钮显示的顺序一致，
    /// 因为其他地方（比如 GenerateDailyPuzzle）会用索引遍历。
    /// </summary>
    public static string[] Categories = { "Kazimierz", "Kjerag", "RhodesIsland", "Ursus", "Victoria", "Yan" };

    /// <summary>
    /// 各分类的解锁价格。
    /// 索引与 Categories 对应，0 表示免费。
    /// 目前所有分类都是 0（全部免费），保留数组是为了后续扩展。
    /// </summary>
    public static int[] CategoryPrices = { 0, 0, 0, 0, 0, 0 };

    /// <summary>
    /// 每个分类的前 N 张图片免费。
    /// 比如每个分类有 10 张图，前 5 张免费，第 6 张开始要花 100 金币解锁。
    /// </summary>
    public const int FreeImagesPerCategory = 5;

    /// <summary>
    /// 解锁一张付费图片需要的金币数（统一价格）。
    /// </summary>
    public const int ImagePrice = 100;

    /// <summary>
    /// "上传分类"的特殊标识。
    /// 在代码里用来判断"当前分类是上传"还是"普通分类"。
    /// 用一个不在 Categories 里的字符串，避免冲突。
    /// </summary>
    public const string UploadCategory = "Upload";

    /// <summary>
    /// "共享分类"的特殊标识。
    /// 与 UploadCategory 同理，用于区分"下载来的图片"。
    /// </summary>
    public const string SharedCategory = "Shared";

    #endregion

    #region PlayerPrefs 键名常量

    // ============================================================
    // 【为什么要定义这些常量？】
    // PlayerPrefs 的 key 是一串字符串（比如 "Coins"）。
    // 如果代码里到处写 "Coins"，一旦拼错（比如写成 "Coin"），
    // 就会读不到数据，而且很难排查。
    // 用 const 定义，编译器会检查拼写，安全可靠。
    // ============================================================

    // ---------- 金币与解锁 ----------
    private const string CoinsKey = "Coins";                        // 金币数量
    private const string UnlockPrefix = "Unlock_";                  // 分类解锁前缀（Unlock_分类名）
    private const string ImageUnlockPrefix = "ImageUnlock_";        // 图片解锁前缀（ImageUnlock_分类_索引）

    // ---------- 玩家信息 ----------
    private const string PlayerNameKey = "PlayerName";              // 玩家名字
    private const string LevelKey = "Level";                        // 等级
    private const string ExperienceKey = "Experience";              // 当前经验
    private const string FavoritesKey = "Favorites";                // 收藏列表（逗号分隔）
    private const string RewardClaimedPrefix = "RewardClaimed_";    // 首通奖励前缀

    // ---------- 体力系统 ----------
    private const string StaminaKey = "Stamina";                    // 当前体力
    private const string LastStaminaTimeKey = "LastStaminaTime";    // 上次体力恢复的时间戳

    // ---------- 每日拼图 ----------
    private const string DailyPuzzleDateKey = "DailyPuzzleDate";                          // 生成日期
    private const string DailyPuzzleImagesKey = "DailyPuzzleImages";                      // 图片列表
    private const string DailyPuzzleDifficultiesKey = "DailyPuzzleDifficulties";          // 难度列表
    private const string DailyPuzzleCompletedKey = "DailyPuzzleCompleted";                // 是否全部完成
    private const string DailyPuzzleCompletedFlagsKey = "DailyPuzzleCompletedFlags";      // 每张的完成标记

    // ---------- 上传与共享图片 ----------
    private const string UploadImagesKey = "UploadImages";          // 已上传文件名列表
    private const string SharedImagesKey = "SharedImages";          // 已共享文件名列表

    // ---------- 头像 ----------
    private const string AvatarIndexKey = "AvatarIndex";            // 当前选中的头像索引

    // ---------- 拼图进度 ----------
    private const string PuzzleProgressPrefix = "PuzzleProgress_";  // 拼图进度前缀

    #endregion

    #region 体力系统常量

    private const int BaseStamina = 100;                       // 初始体力上限
    private const int StaminaIncreasePer5Levels = 50;          // 每 5 级增加的上限
    private const int StaminaRecoveryIntervalSeconds = 60;     // 每 60 秒恢复 1 点
    private const int StaminaRecoveryAmount = 1;               // 每次恢复量

    /// <summary>
    /// 每局拼图消耗的体力值。
    /// public 是因为 GameManager 里要读这个值判断够不够体力。
    /// </summary>
    public const int PuzzleStaminaCost = 10;

    #endregion

    #region 每日拼图常量

    /// <summary>
    /// 每日拼图的总张数。
    /// 每天生成 10 张，全部完成后能领奖励。
    /// </summary>
    public const int DailyPuzzleCount = 10;

    #endregion

    #region 体力系统

    /// <summary>
    /// 当前体力值。
    /// 
    /// 【关键设计】
    /// get 时先调用 UpdateStaminaRecovery()，
    /// 它会根据"上次保存的时间戳"和"现在的时间"算出这段时间应该恢复多少体力。
    /// 这样即使用户关掉游戏好几天，再次打开也能自动恢复。
    /// 
    /// 【为什么 set 是 private？】
    /// 防止外部代码直接赋值，避免逻辑遗漏（比如忘了存档）。
    /// 外部要通过 AddStamina / ConsumeStamina 修改。
    /// </summary>
    public static int Stamina
    {
        get
        {
            UpdateStaminaRecovery();                        // 先处理离线恢复
            return PlayerPrefs.GetInt(StaminaKey, MaxStamina);   // 读当前体力
        }
        private set
        {
            PlayerPrefs.SetInt(StaminaKey, value);          // 写体力
            PlayerPrefs.Save();                             // 立即落盘
        }
    }

    /// <summary>
    /// 体力上限（动态计算，随等级增长）。
    /// 
    /// 【公式】
    /// 等级 1-4   → 100
    /// 等级 5-9   → 150
    /// 等级 10-14 → 200
    /// 等级 15-19 → 250
    /// 以此类推……
    /// 
    /// 【计算过程】
    /// (Level - 1) / 5 是整数除法：
    ///   等级 1  → 0/5 = 0
    ///   等级 4  → 3/5 = 0
    ///   等级 5  → 4/5 = 0？ 不对——等等，等级 5 时 (5-1)/5 = 0
    ///   等级 9  → 8/5 = 1
    ///   等级 10 → 9/5 = 1
    /// 所以 1-4 级是第 0 组，5-9 级是第 1 组，10-14 是第 2 组。
    /// </summary>
    public static int MaxStamina
    {
        get
        {
            int levelGroup = (Level - 1) / 5;   // 整数除法，自动取整
            return BaseStamina + levelGroup * StaminaIncreasePer5Levels;
        }
    }

    /// <summary>
    /// 增加体力（不会超过上限）。
    /// 用于"看广告恢复"、"每日奖励"等场景。
    /// </summary>
    public static void AddStamina(int amount)
    {
        UpdateStaminaRecovery();   // 先处理离线恢复
        // Mathf.Min 保证不会超过上限
        Stamina = Mathf.Min(MaxStamina, Stamina + amount);
    }

    /// <summary>
    /// 消耗体力。
    /// 返回 true = 消耗成功；返回 false = 体力不够。
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
    /// 
    /// 【核心思路】
    /// 1. 读取上次记录的时间戳
    /// 2. 计算距离现在过了多少秒
    /// 3. 每 60 秒恢复 1 点
    /// 4. 更新"上次恢复时间"，只减去完整的恢复周期，保留零头
    ///    （否则"59 秒 + 59 秒"会浪费，正确应该算作 1 次恢复 + 58 秒）
    /// 
    /// 【为什么要保留零头？】
    /// 假设每 60 秒恢复 1 点。用户 30 秒后打开看一眼，又关掉。
    /// 再过 30 秒打开，如果每次都清零，永远恢复不了。
    /// 保留零头的话，第 60 秒就能恢复 1 点。
    /// </summary>
    private static void UpdateStaminaRecovery()
    {
        // 读取上次体力恢复时间（用 Ticks 存储，高精度）
        long lastTimeTicks = long.Parse(
            PlayerPrefs.GetString(LastStaminaTimeKey, DateTime.UtcNow.Ticks.ToString()));

        // 把 Ticks 转回 DateTime
        DateTime lastTime = new DateTime(lastTimeTicks);

        // 距现在过了多久
        TimeSpan elapsed = DateTime.UtcNow - lastTime;

        // 应该恢复几次（整数除法）
        int recoveryCount = (int)(elapsed.TotalSeconds / StaminaRecoveryIntervalSeconds);

        if (recoveryCount > 0)
        {
            // 读取当前体力
            int currentStamina = PlayerPrefs.GetInt(StaminaKey, MaxStamina);

            // 加上恢复的体力，但不超过上限
            int newStamina = Mathf.Min(MaxStamina, currentStamina + recoveryCount * StaminaRecoveryAmount);
            PlayerPrefs.SetInt(StaminaKey, newStamina);

            // 只减去"完整周期"的秒数，保留不足一次的零头
            // 例：过了 130 秒，恢复 2 次（120 秒），保留 10 秒零头
            DateTime newLastTime = lastTime.AddSeconds(recoveryCount * StaminaRecoveryIntervalSeconds);
            PlayerPrefs.SetString(LastStaminaTimeKey, newLastTime.Ticks.ToString());
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 初始化体力系统。
    /// 只在"首次启动游戏"时执行，设置满体力和初始时间戳。
    /// 用 HasKey 判断是否已经初始化过。
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
    /// 
    /// 【为什么用 get => 表达式？】
    /// 这是 C# 的"表达式体成员"语法，等价于：
    ///   get { return PlayerPrefs.GetInt(CoinsKey, 0); }
    /// 更简洁。
    /// 
    /// 【为什么要 Save()？】
    /// PlayerPrefs.SetInt 只写到内存缓存。
    /// Save() 才真正落盘（写文件/注册表）。
    /// 不 Save 的话，游戏崩溃或强制退出可能丢数据。
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

    /// <summary>
    /// 消费金币。
    /// 余额足够 → 扣除并返回 true；
    /// 余额不足 → 不扣，返回 false。
    /// </summary>
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
    /// 检查某张图的某个难度是否已经领取过金币奖励。
    /// 
    /// 【什么是"首通"？】
    /// 第一次拼完某张图某个难度，会奖励金币。
    /// 再拼一遍只有经验，没有金币。
    /// 
    /// 【key 格式】
    /// RewardClaimed_分类名_图片索引_难度
    /// 例：RewardClaimed_Kazimierz_3_8
    /// </summary>
    public static bool HasClaimedReward(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        return PlayerPrefs.GetInt(key, 0) == 1;
    }

    /// <summary>标记某张图的某个难度已领取奖励。</summary>
    public static void SetRewardClaimed(string category, int imageIndex, int gridSize)
    {
        string key = RewardClaimedPrefix + category + "_" + imageIndex + "_" + gridSize;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
    }

    #endregion

    #region 分类与图片解锁

    /// <summary>
    /// 判断分类是否解锁。
    /// 目前所有分类都免费，直接返回 true。
    /// 保留方法是为了后续加付费分类时方便。
    /// </summary>
    public static bool IsCategoryUnlocked(string category)
    {
        return true;
    }

    /// <summary>解锁分类。</summary>
    public static void UnlockCategory(string category)
    {
        PlayerPrefs.SetInt(UnlockPrefix + category, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 判断某分类下第 imageIndex 张图片是否解锁。
    /// 
    /// 【规则】
    /// 前 FreeImagesPerCategory 张免费；
    /// 第 6 张及以后需要购买。
    /// </summary>
    public static bool IsImageUnlocked(string category, int imageIndex)
    {
        // 前 N 张免费
        if (imageIndex < FreeImagesPerCategory) return true;

        // 其他看是否买过
        return PlayerPrefs.GetInt(ImageUnlockPrefix + category + "_" + imageIndex, 0) == 1;
    }

    /// <summary>解锁图片。</summary>
    public static void UnlockImage(string category, int imageIndex)
    {
        PlayerPrefs.SetInt(ImageUnlockPrefix + category + "_" + imageIndex, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 获取图片价格。
    /// 目前统一 100 金币，保留方法方便后续做"难度定价"。
    /// </summary>
    public static int GetImagePrice(string category, int imageIndex)
    {
        return ImagePrice;
    }

    #endregion

    #region 玩家信息（名字、等级、经验）

    /// <summary>
    /// 玩家名字。
    /// 有 get 和 set，因为用户可以在设置里修改。
    /// </summary>
    public static string PlayerName
    {
        get => PlayerPrefs.GetString(PlayerNameKey, "");
        set { PlayerPrefs.SetString(PlayerNameKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// 玩家等级。
    /// set 是 private，因为等级只能通过 AddExperience 自动提升。
    /// </summary>
    public static int Level
    {
        get => PlayerPrefs.GetInt(LevelKey, 1);   // 默认 1 级
        private set { PlayerPrefs.SetInt(LevelKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// 当前经验（未满一级的部分）。
    /// 升级时会被消耗掉。
    /// </summary>
    public static int Experience
    {
        get => PlayerPrefs.GetInt(ExperienceKey, 0);
        private set { PlayerPrefs.SetInt(ExperienceKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// 增加经验并自动处理升级。
    /// 
    /// 【核心逻辑】
    /// while 循环：只要经验 >= 升级需求，就升级、扣经验、再检查。
    /// 用 while 而不是 if，是因为一次可能连升多级
    /// （比如奖励 100 经验，而 1 级升 2 级只需 10）。
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
    /// 计算指定等级升级到下一级需要的经验值。
    /// 公式：5 × (等级 + 1)
    ///   1 级 → 10
    ///   2 级 → 15
    ///   3 级 → 20
    ///   ...
    /// </summary>
    public static int GetRequiredExperience(int level)
    {
        return 5 * (level + 1);
    }

    #endregion

    #region 收藏夹

    /// <summary>
    /// 获取收藏列表。
    /// 
    /// 【存储格式】
    /// 用逗号拼接的字符串，例："Kazimierz_0,RhodesIsland_3"
    /// 每个元素格式：分类名_图片索引
    /// 
    /// 【为什么要自定义格式？】
    /// PlayerPrefs 只支持存 int、float、string。
    /// 存 List 要先序列化成字符串，读取时再 split 回来。
    /// </summary>
    public static List<string> GetFavorites()
    {
        string saved = PlayerPrefs.GetString(FavoritesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>添加收藏（如果还没收藏）。</summary>
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

    /// <summary>判断某张图是否已收藏。</summary>
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
    /// 检查今天的每日拼图是否已经生成过。
    /// 用日期字符串（yyyyMMdd）比较。
    /// </summary>
    public static bool IsDailyPuzzleGeneratedToday()
    {
        return PlayerPrefs.GetString(DailyPuzzleDateKey, "") == DateTime.UtcNow.ToString("yyyyMMdd");
    }

    /// <summary>
    /// 检查今天的每日拼图是否全部完成。
    /// 必须先"已生成" + "Completed 标记为 1"。
    /// </summary>
    public static bool IsDailyPuzzleCompletedToday()
    {
        return IsDailyPuzzleGeneratedToday() && PlayerPrefs.GetInt(DailyPuzzleCompletedKey, 0) == 1;
    }

    /// <summary>
    /// 生成新的每日拼图。
    /// 
    /// 【流程】
    /// 1. 收集所有分类的所有图片
    /// 2. 随机抽 10 张（不重复）
    /// 3. 给每张随机分配难度
    /// 4. 存入 PlayerPrefs
    /// 
    /// 【为什么要随机难度？】
    /// 让每日拼图有变化，玩家不知道会遇到简单还是困难的。
    /// </summary>
    public static void GenerateDailyPuzzle()
    {
        // 收集所有分类的所有图片（用 "分类_索引" 格式）
        List<string> allImages = new List<string>();
        foreach (string category in Categories)
        {
            Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites(category);
            for (int i = 0; i < sprites.Length; i++)
                allImages.Add(category + "_" + i);
        }

        // 准备索引列表（用于随机抽取）
        List<int> indices = new List<int>();
        for (int i = 0; i < allImages.Count; i++) indices.Add(i);

        List<string> selectedImages = new List<string>();
        List<int> selectedDifficulties = new List<int>();

        // 可选难度：2×2 / 8×8 / 10×10
        int[] difficulties = { 2, 8, 10 };  // 测试用，正式可改为 { 6, 8, 10 }

        // 抽 10 张
        for (int i = 0; i < DailyPuzzleCount; i++)
        {
            if (indices.Count == 0) break;   // 图片不够就提前结束

            // 随机选一个索引
            int randIdx = UnityEngine.Random.Range(0, indices.Count);
            int imageIdx = indices[randIdx];
            indices.RemoveAt(randIdx);   // 移除，防止重复选中

            selectedImages.Add(allImages[imageIdx]);

            // 随机难度
            selectedDifficulties.Add(difficulties[UnityEngine.Random.Range(0, difficulties.Length)]);
        }

        // 写入 PlayerPrefs
        PlayerPrefs.SetString(DailyPuzzleDateKey, DateTime.UtcNow.ToString("yyyyMMdd"));
        PlayerPrefs.SetString(DailyPuzzleImagesKey, string.Join(",", selectedImages));
        PlayerPrefs.SetString(DailyPuzzleDifficultiesKey,
            string.Join(",", selectedDifficulties.ConvertAll(x => x.ToString())));
        PlayerPrefs.SetInt(DailyPuzzleCompletedKey, 0);   // 新的一天，未完成
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 获取每日拼图每张的完成状态。
    /// 返回的 bool 列表长度可能小于 DailyPuzzleCount（未完成的默认为 false）。
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
    /// 设置每日拼图中某张的完成状态。
    /// </summary>
    public static void SetDailyPuzzleImageCompleted(int imageIndex, bool completed)
    {
        List<bool> flags = GetDailyPuzzleCompletedFlags();

        // 如果列表长度不够，先补 false 到指定索引
        while (flags.Count <= imageIndex) flags.Add(false);
        flags[imageIndex] = completed;

        // 转成 "1,0,1,0..." 格式存储
        PlayerPrefs.SetString(DailyPuzzleCompletedFlagsKey,
            string.Join(",", flags.ConvertAll(x => x ? "1" : "0")));
        PlayerPrefs.Save();
    }

    /// <summary>检查每日拼图中某张是否已完成。</summary>
    public static bool IsDailyPuzzleImageCompleted(int imageIndex)
    {
        List<bool> flags = GetDailyPuzzleCompletedFlags();
        if (imageIndex < flags.Count) return flags[imageIndex];
        return false;   // 列表长度不够 = 肯定未完成
    }

    /// <summary>获取每日拼图的图片列表。</summary>
    public static List<string> GetDailyPuzzleImages()
    {
        string saved = PlayerPrefs.GetString(DailyPuzzleImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>获取每日拼图的难度列表。</summary>
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

    /// <summary>标记每日拼图全部完成。</summary>
    public static void SetDailyPuzzleCompleted()
    {
        PlayerPrefs.SetInt(DailyPuzzleCompletedKey, 1);
        PlayerPrefs.Save();
    }

    #endregion

    #region 上传图片

    /// <summary>
    /// 获取所有已上传图片的文件名列表。
    /// 格式：逗号分隔，例："upload_20260912_004224_386.png,upload_xxx.png"
    /// </summary>
    public static List<string> GetUploadedImages()
    {
        string saved = PlayerPrefs.GetString(UploadImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>
    /// 添加一个上传图片文件名（如果还没记录过）。
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
    /// 
    /// 【两步走】
    /// 1. 从 PlayerPrefs 列表里移除
    /// 2. 从磁盘上删除文件
    /// </summary>
    public static void RemoveUploadedImage(string fileName)
    {
        List<string> list = GetUploadedImages();

        if (list.Remove(fileName))
        {
            PlayerPrefs.SetString(UploadImagesKey, string.Join(",", list));
            PlayerPrefs.Save();

            // 删除磁盘文件（存在才删，避免报错）
            string filePath = GetUploadedImagePath(fileName);
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    /// <summary>
    /// 获取上传图片的完整路径。
    /// 例：/storage/emulated/0/Android/data/.../files/Uploads/xxx.png
    /// </summary>
    public static string GetUploadedImagePath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, "Uploads", fileName);
    }

    #endregion

    #region 共享图片

    /// <summary>
    /// 获取所有共享图片的文件名列表。
    /// 结构同上传图片。
    /// </summary>
    public static List<string> GetSharedImages()
    {
        string saved = PlayerPrefs.GetString(SharedImagesKey, "");
        if (string.IsNullOrEmpty(saved)) return new List<string>();
        return new List<string>(saved.Split(','));
    }

    /// <summary>添加一个共享图片文件名。</summary>
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
    /// 共享图片存在 Shared 目录（区别于上传的 Uploads 目录）。
    /// </summary>
    public static string GetSharedImagePath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, "Shared", fileName);
    }

    #endregion

    #region 头像

    /// <summary>
    /// 获取当前选中的头像索引。
    /// 头像资源放在 Resources/Art/HeadPicture 下。
    /// </summary>
    public static int GetAvatarIndex()
    {
        return PlayerPrefs.GetInt(AvatarIndexKey, 0);   // 默认第 0 个
    }

    /// <summary>设置当前头像索引。</summary>
    public static void SetAvatarIndex(int index)
    {
        PlayerPrefs.SetInt(AvatarIndexKey, index);
        PlayerPrefs.Save();
    }

    #endregion

    #region 拼图进度

    /// <summary>
    /// 生成进度存储的 key。
    /// 
    /// 【key 格式】
    /// PuzzleProgress_分类_图片索引_难度
    /// 例：PuzzleProgress_Kazimierz_3_8
    /// 
    /// 【为什么要区分难度？】
    /// 同一张图的 2×2 和 8×8 是两个独立的拼图，
    /// 进度互不影响。
    /// </summary>
    private static string GetProgressKey(string category, int imageIndex, int gridSize)
    {
        return PuzzleProgressPrefix + category + "_" + imageIndex + "_" + gridSize;
    }

    /// <summary>
    /// 保存拼图进度。
    /// 
    /// 【存储格式】
    /// 用 "0/1" 组成的字符串，长度 = totalPieces。
    /// 第 i 位 = '1' 表示第 i 号碎片已锁定。
    /// 例：4 块碎片，锁了 1 号和 3 号 → "1010"
    /// 
    /// 【为什么用字符串？】
    /// PlayerPrefs 不能存 bool 数组，只能转成字符串。
    /// 
    /// 【优化】
    /// 如果全部为 false（没有任何锁定），直接删除记录，
    /// 避免存一堆无意义的 "0000..."。
    /// </summary>
    public static void SavePuzzleProgress(string category, int imageIndex, int gridSize, bool[] lockedFlags)
    {
        // 空数组视为无进度
        if (lockedFlags == null || lockedFlags.Length == 0)
        {
            ClearPuzzleProgress(category, imageIndex, gridSize);
            return;
        }

        // 检查是否至少有一个 true
        bool hasAny = false;
        for (int i = 0; i < lockedFlags.Length; i++)
        {
            if (lockedFlags[i]) { hasAny = true; break; }
        }

        // 一个都没有 → 清除记录
        if (!hasAny)
        {
            ClearPuzzleProgress(category, imageIndex, gridSize);
            return;
        }

        // 用 StringBuilder 拼 "0/1" 字符串
        // StringBuilder 比字符串拼接（+=）效率高很多
        var sb = new System.Text.StringBuilder(lockedFlags.Length);
        for (int i = 0; i < lockedFlags.Length; i++)
            sb.Append(lockedFlags[i] ? '1' : '0');

        PlayerPrefs.SetString(GetProgressKey(category, imageIndex, gridSize), sb.ToString());
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 读取拼图进度。
    /// 
    /// 【返回值】
    /// - 有记录 + 长度匹配 → bool[] 数组
    /// - 无记录 → null
    /// - 长度不匹配 → null（比如老版本存档的长度和当前难度不一致）
    /// 
    /// 【为什么校验长度？】
    /// 假设用户之前玩 8×8 存了 64 位，
    /// 现在游戏更新成 10×10（100 块），
    /// 长度不匹配就要作废旧记录，避免数组越界。
    /// </summary>
    public static bool[] LoadPuzzleProgress(string category, int imageIndex, int gridSize, int totalPieces)
    {
        string key = GetProgressKey(category, imageIndex, gridSize);

        // 无记录
        if (!PlayerPrefs.HasKey(key)) return null;

        string s = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(s)) return null;

        // 长度校验
        if (s.Length != totalPieces) return null;

        // 逐位转 bool
        bool[] result = new bool[totalPieces];
        for (int i = 0; i < totalPieces; i++)
            result[i] = s[i] == '1';

        return result;
    }

    /// <summary>清除某张图的进度记录。</summary>
    public static void ClearPuzzleProgress(string category, int imageIndex, int gridSize)
    {
        PlayerPrefs.DeleteKey(GetProgressKey(category, imageIndex, gridSize));
        PlayerPrefs.Save();
    }

    /// <summary>判断某张图是否有存档进度。</summary>
    public static bool HasPuzzleProgress(string category, int imageIndex, int gridSize)
    {
        return PlayerPrefs.HasKey(GetProgressKey(category, imageIndex, gridSize));
    }

    #endregion

    #region 编辑器工具

    /// <summary>
    /// 重置所有数据（仅在 Unity 编辑器里有效）。
    /// 
    /// 【用途】
    /// 测试时清空所有 PlayerPrefs，模拟"新玩家首次启动"。
    /// 
    /// 【为什么用 #if UNITY_EDITOR？】
    /// 防止正式发布时误触发。打包后这段代码不会被编译进去。
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