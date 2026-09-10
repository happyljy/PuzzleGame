using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// AssetBundle 管理器：负责在游戏启动时从 StreamingAssets 目录加载所有 AB 包，
/// 并按图片名称前缀（分类名）将其分组缓存，供游戏其他模块按分类获取 Sprite。
/// 
/// 图片命名规范：分类名_序号，例如 "RhodesIsland_0"、"Kjerag_3"。
/// 使用示例：
/// <code>
/// Sprite[] sprites = AssetBundleManager.Instance.GetCategorySprites("RhodesIsland");
/// </code>
/// </summary>
public class AssetBundleManager : MonoBehaviour
{
    #region 单例

    public static AssetBundleManager Instance { get; private set; }

    #endregion

    #region 公共属性

    /// <summary>AB 包是否已全部加载完成。</summary>
    public bool IsLoaded { get; private set; } = false;

    #endregion

    #region 私有字段

    // 分类名 → 该分类下所有 Sprite 的列表
    private Dictionary<string, List<Sprite>> categorySprites = new Dictionary<string, List<Sprite>>();

    // 主 manifest 文件名（与构建时 AssetBundle 名称保持一致）
    private const string MainManifestName = "AssetBundles";

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(LoadAllBundles());
    }

    #endregion

    #region 加载流程

    /// <summary>
    /// 从 StreamingAssets/AssetBundles 目录加载主 manifest 及所有子 AB 包。
    /// </summary>
    private IEnumerator LoadAllBundles()
    {
        string bundleDir = System.IO.Path.Combine(Application.streamingAssetsPath, "AssetBundles");
        string manifestPath = System.IO.Path.Combine(bundleDir, MainManifestName);

        // 1. 加载主 manifest
        AssetBundleManifest manifest = null;
        yield return LoadManifest(manifestPath, (result) => manifest = result);

        if (manifest == null)
        {
            Debug.LogError("AssetBundle 加载终止：无法获取主 manifest。");
            yield break;
        }

        // 2. 遍历所有子 AB 包并加载
        string[] bundleNames = manifest.GetAllAssetBundles();
        foreach (string bundleName in bundleNames)
        {
            string path = System.IO.Path.Combine(bundleDir, bundleName);
            yield return LoadSingleBundle(path, bundleName);
        }

        // 3. 对每个分类内的 Sprite 按名称排序
        foreach (var kvp in categorySprites)
        {
            kvp.Value.Sort((a, b) => string.Compare(a.name, b.name));
        }

        IsLoaded = true;
        Debug.Log($"AssetBundle 加载完成，共 {categorySprites.Count} 个分类");
    }

    /// <summary>
    /// 加载主 manifest，并通过回调返回 <see cref="AssetBundleManifest"/>。
    /// </summary>
    private IEnumerator LoadManifest(string manifestPath, System.Action<AssetBundleManifest> onLoaded)
    {
        UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(manifestPath);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("无法加载 AssetBundle manifest: " + request.error);
            onLoaded?.Invoke(null);
            yield break;
        }

        AssetBundle manifestBundle = DownloadHandlerAssetBundle.GetContent(request);
        AssetBundleManifest manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        onLoaded?.Invoke(manifest);
    }

    /// <summary>
    /// 加载单个 AB 包，并将其中的所有 Sprite 按分类解析到字典中。
    /// </summary>
    private IEnumerator LoadSingleBundle(string path, string bundleName)
    {
        UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(path);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"加载 {bundleName} 失败: {request.error}");
            yield break;
        }

        AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
        Sprite[] sprites = bundle.LoadAllAssets<Sprite>();

        foreach (Sprite sprite in sprites)
        {
            RegisterSprite(sprite);
        }

        // 卸载 AB 本身，但保留已加载的资源（Sprite）
        bundle.Unload(false);
    }

    /// <summary>
    /// 根据 Sprite 名字解析分类，并加入字典。
    /// 名字格式要求：分类名_序号，例如 "RhodesIsland_0"。
    /// </summary>
    private void RegisterSprite(Sprite sprite)
    {
        string[] parts = sprite.name.Split('_');
        if (parts.Length < 2)
        {
            Debug.LogWarning($"Sprite 名字格式错误，已跳过: {sprite.name}");
            return;
        }

        string category = parts[0];
        if (!categorySprites.ContainsKey(category))
            categorySprites[category] = new List<Sprite>();

        categorySprites[category].Add(sprite);
    }

    #endregion

    #region 公共接口

    /// <summary>
    /// 获取指定分类下的所有 Sprite。若分类不存在则返回空数组。
    /// </summary>
    public Sprite[] GetCategorySprites(string category)
    {
        if (categorySprites.TryGetValue(category, out var list))
            return list.ToArray();
        return new Sprite[0];
    }

    #endregion
}