using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// AssetBundle 管理器。
///
/// 【AssetBundle 是什么？】
/// 可以理解为"Unity 打包出来的资源压缩包"。
/// 游戏里的大量图片、音频、模型可以不用直接塞进 APK，
/// 而是打包成独立的 AB 文件放在手机里，游戏运行时按需加载。
/// 好处：
///   - APK 体积更小
///   - 资源可以单独更新（不用重新下载整个游戏）
///   - 内存管理更可控（用完了可以卸载）
///
/// 【本脚本做什么？】
/// 游戏启动时，从 StreamingAssets/AssetBundles 文件夹加载所有 AB 包，
/// 把里面所有 Sprite（图片）按名字前缀（也就是"分类名"）分好组，
/// 然后缓存到内存里，供游戏其他模块随时按分类取用。
///
/// 【命名规范】
/// 图片名字必须是 分类名_序号 的格式，例如：
///   - "Kazimierz_0"、"Kazimierz_1"（喀兹米日分类的第 0、1 张）
///   - "Kjerag_3"（谢拉格分类的第 3 张）
/// 脚本会按 "_" 切分，把 "_" 前面那部分当作分类名。
///
/// 【使用示例】
/// <code>
/// Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites("RhodesIsland");
/// </code>
/// </summary>
public class AssetBundleManager : MonoBehaviour
{
    #region 单例

    /// <summary>
    /// 全局唯一的实例。
    /// 通过 AssetBundleManager.Instance 访问，确保整个游戏只有一个管理器。
    /// </summary>
    public static AssetBundleManager Instance { get; private set; }

    #endregion

    #region 公共属性

    /// <summary>
    /// AB 包是否已全部加载完成。
    /// 外部代码可以用 while (!AssetBundleManager.Instance.IsLoaded) 等待加载完毕。
    /// </summary>
    public bool IsLoaded { get; private set; } = false;

    #endregion

    #region 私有字段

    /// <summary>
    /// 分类名 → 该分类下所有 Sprite 的列表。
    /// 
    /// 【数据结构说明】
    /// Dictionary 是"键值对"容器，类似一个查字典的过程：
    ///   - 键（key）   = 分类名，例如 "Kazimierz"
    ///   - 值（value） = 该分类下所有图片 Sprite 的列表
    ///
    /// 例：
    ///   categorySprites["Kazimierz"] = [Kazimierz_0, Kazimierz_1, ...]
    ///   categorySprites["Kjerag"]    = [Kjerag_0, Kjerag_1, ...]
    /// </summary>
    private Dictionary<string, List<Sprite>> categorySprites = new Dictionary<string, List<Sprite>>();

    /// <summary>
    /// 主 manifest 文件名。
    /// 每次打包 AB 时，Unity 都会自动生成一个"主包"，记录所有子包之间的依赖关系。
    /// 这里的名字要和构建脚本 (BuildAssetBundles.cs) 里设置的名字保持一致。
    /// </summary>
    private const string MainManifestName = "AssetBundles";

    #endregion

    #region Unity 生命周期

    /// <summary>
    /// Awake 在物体被创建时调用，早于 Start。
    /// 这里用来实现"单例保护"。
    /// </summary>
    private void Awake()
    {
        // 如果已经有一个实例存在，且不是自己，就把自己销毁掉
        // 这样保证全局只有一个 AssetBundleManager
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 把自己设为全局唯一实例
        Instance = this;

        // 切换场景时不销毁这个物体，保证 AB 缓存常驻
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Start 在物体第一帧启用时调用。
    /// 这里启动加载 AB 包的协程。
    /// </summary>
    private void Start()
    {
        // 启动协程（异步加载，不阻塞主线程）
        StartCoroutine(LoadAllBundles());
    }

    #endregion

    #region 加载流程

    /// <summary>
    /// 从 StreamingAssets/AssetBundles 目录加载主 manifest 及所有子 AB 包。
    ///
    /// 【为什么用协程（IEnumerator）？】
    /// 加载 AB 包是 IO 操作，可能要几百毫秒到几秒。
    /// 如果用普通同步方法，游戏会卡住（黑屏或 UI 冻结）。
    /// 用协程可以让出主线程，边加载边保持 UI 响应。
    /// </summary>
    private IEnumerator LoadAllBundles()
    {
        // ---------- 第 1 步：确定 AB 目录和主 manifest 的路径 ----------
        // Application.streamingAssetsPath 是 Unity 内置的"只读资源目录"，
        // 打包 APK 时会原样拷贝进去。
        // 在 Android 上它的值类似：jar:file:///data/app/.../base.apk!/assets
        // 在 PC 上就是：<项目目录>/Assets/StreamingAssets
        string bundleDir = System.IO.Path.Combine(Application.streamingAssetsPath, "AssetBundles");
        string manifestPath = System.IO.Path.Combine(bundleDir, MainManifestName);

        // ---------- 第 2 步：加载主 manifest ----------
        AssetBundleManifest manifest = null;

        // yield return 会等待协程执行完再继续
        // 这里等 LoadManifest 完成后，通过回调把结果赋给 manifest
        yield return LoadManifest(manifestPath, (result) => manifest = result);

        // manifest 为 null 说明主包加载失败，直接终止
        if (manifest == null)
        {
            Debug.LogError("AssetBundle 加载终止：无法获取主 manifest。");
            yield break;   // yield break 结束协程
        }

        // ---------- 第 3 步：遍历所有子 AB 包并逐个加载 ----------
        // manifest.GetAllAssetBundles() 返回所有子 AB 包的名字数组。
        // 例如 ["kazimierz", "kjerag", "rhodesisland", ...]
        string[] bundleNames = manifest.GetAllAssetBundles();
        foreach (string bundleName in bundleNames)
        {
            // 拼出每个子包的完整路径
            string path = System.IO.Path.Combine(bundleDir, bundleName);

            // 逐个加载（yield 让它一个接一个，不会同时加载）
            yield return LoadSingleBundle(path, bundleName);
        }

        // ---------- 第 4 步：对每个分类内的 Sprite 按名称排序 ----------
        // 这样保证 GetCategorySprites 返回的顺序是稳定的，
        // 而不是依赖 AB 里的随机顺序。
        foreach (var kvp in categorySprites)
        {
            // Sort 传一个比较函数：按名字的字典序排列
            // string.Compare(a, b) 的语义：
            //   负数 → a 排在 b 前面
            //   0    → a、b 相等
            //   正数 → a 排在 b 后面
            kvp.Value.Sort((a, b) => string.Compare(a.name, b.name));
        }

        // ---------- 第 5 步：标记加载完成 ----------
        IsLoaded = true;
        Debug.Log($"AssetBundle 加载完成，共 {categorySprites.Count} 个分类");
    }

    /// <summary>
    /// 加载主 manifest，并通过回调返回 AssetBundleManifest。
    ///
    /// 【什么是 manifest？】
    /// 它记录了"哪些 AB 包存在""它们之间有什么依赖"。
    /// 加载子包之前必须先加载它，才能知道要加载哪些子包。
    /// </summary>
    /// <param name="manifestPath">主 manifest 文件的完整路径</param>
    /// <param name="onLoaded">加载完成后的回调，参数是 manifest（失败时传 null）</param>
    private IEnumerator LoadManifest(string manifestPath, System.Action<AssetBundleManifest> onLoaded)
    {
        // UnityWebRequestAssetBundle 是 Unity 提供的"加载 AB 包"专用请求。
        // 它能统一处理 PC（file://）和 Android（jar:file://）的差异，
        // 比自己拼路径 + File.ReadAllBytes 可靠得多。
        UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(manifestPath);

        // yield return 会挂起协程，直到请求完成
        // 期间 Unity 主线程不会被卡住
        yield return request.SendWebRequest();

        // 请求失败（网络错误、文件不存在、格式错误等）
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("无法加载 AssetBundle manifest: " + request.error);
            onLoaded?.Invoke(null);   // ?.Invoke 表示"如果回调不为 null 就调用"
            yield break;
        }

        // 从请求里取出 AB 包对象
        AssetBundle manifestBundle = DownloadHandlerAssetBundle.GetContent(request);

        // 从 manifest 包中加载 AssetBundleManifest 资源
        // 注意：这个资源的名字固定是 "AssetBundleManifest"，不要改
        AssetBundleManifest manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");

        // 通过回调返回给上层
        onLoaded?.Invoke(manifest);
    }

    /// <summary>
    /// 加载单个 AB 包，并将其中的所有 Sprite 按分类解析到字典中。
    /// </summary>
    /// <param name="path">AB 文件的完整路径</param>
    /// <param name="bundleName">AB 包的名字（用于日志）</param>
    private IEnumerator LoadSingleBundle(string path, string bundleName)
    {
        // 用 UnityWebRequest 异步加载单个 AB
        UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(path);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"加载 {bundleName} 失败: {request.error}");
            yield break;
        }

        // 拿到 AB 包对象
        AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);

        // 加载 AB 包里所有 Sprite 类型的资源
        Sprite[] sprites = bundle.LoadAllAssets<Sprite>();

        // 逐个登记到字典里
        foreach (Sprite sprite in sprites)
        {
            RegisterSprite(sprite);
        }

        // ---------- 卸载 AB 包本身 ----------
        // Unload(false) 的参数含义：
        //   false = 只卸载"包的结构"，已加载的 Sprite 保留（推荐）
        //   true  = 连包里加载出来的资源也一起销毁（会导致 Sprite 变成 pink 方块）
        //
        // 我们用的是 false，因为我们还要继续用这些 Sprite。
        bundle.Unload(false);
    }

    /// <summary>
    /// 根据 Sprite 名字解析分类，并加入字典。
    /// 
    /// 名字格式要求：分类名_序号，例如 "RhodesIsland_0"。
    /// </summary>
    private void RegisterSprite(Sprite sprite)
    {
        // 用 "_" 分割名字
        // 例："Kazimierz_3" → ["Kazimierz", "3"]
        string[] parts = sprite.name.Split('_');

        // 至少要有 2 部分才说明格式正确
        if (parts.Length < 2)
        {
            Debug.LogWarning($"Sprite 名字格式错误，已跳过: {sprite.name}");
            return;
        }

        // 第一部分是分类名
        string category = parts[0];

        // 如果字典里还没有这个分类，就先创建一个空列表
        if (!categorySprites.ContainsKey(category))
            categorySprites[category] = new List<Sprite>();

        // 把当前 Sprite 加入对应分类的列表
        categorySprites[category].Add(sprite);
    }

    #endregion

    #region 公共接口

    /// <summary>
    /// 获取指定分类下的所有 Sprite。若分类不存在则返回空数组。
    ///
    /// 【为什么返回数组而不是 List？】
    /// 数组是"只读快照"，外部代码拿到后无法修改内部缓存，
    /// 防止误操作破坏数据。
    /// </summary>
    public Sprite[] GetCategorySprites(string category)
    {
        // TryGetValue：如果字典里有这个 key，就取出 value 并返回 true
        // 比 ContainsKey + [] 取值的写法少一次查找
        if (categorySprites.TryGetValue(category, out var list))
            return list.ToArray();

        // 分类不存在时返回空数组（而不是 null），
        // 调用方就不用写 if (x == null) 的检查了
        return new Sprite[0];
    }

    #endregion
}