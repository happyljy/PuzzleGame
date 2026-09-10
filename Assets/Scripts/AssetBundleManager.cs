using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class AssetBundleManager : MonoBehaviour
{
    public static AssetBundleManager Instance { get; private set; }

    private Dictionary<string, List<Sprite>> categorySprites = new Dictionary<string, List<Sprite>>();
    public bool IsLoaded { get; private set; } = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        StartCoroutine(LoadAllBundles());
    }

    IEnumerator LoadAllBundles()
    {
        string bundleDir = System.IO.Path.Combine(Application.streamingAssetsPath, "AssetBundles");
        string manifestPath = System.IO.Path.Combine(bundleDir, "AssetBundles");

        // 1. 加载主 manifest
        UnityWebRequest manifestRequest = UnityWebRequestAssetBundle.GetAssetBundle(manifestPath);
        yield return manifestRequest.SendWebRequest();

        if (manifestRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("无法加载 AssetBundle manifest: " + manifestRequest.error);
            yield break;
        }

        AssetBundle manifestBundle = DownloadHandlerAssetBundle.GetContent(manifestRequest);
        AssetBundleManifest manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");

        // 2. 加载所有子 AB 包
        string[] bundleNames = manifest.GetAllAssetBundles();
        foreach (string bundleName in bundleNames)
        {
            string path = System.IO.Path.Combine(bundleDir, bundleName);
            UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(path);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"加载 {bundleName} 失败: {request.error}");
                continue;
            }

            AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
            Sprite[] sprites = bundle.LoadAllAssets<Sprite>();

            foreach (Sprite sprite in sprites)
            {
                // 解析名字：例如 "1_0" → 分类 "1"
                string[] parts = sprite.name.Split('_');
                if (parts.Length < 2) continue;

                string category = parts[0];
                if (!categorySprites.ContainsKey(category))
                    categorySprites[category] = new List<Sprite>();

                categorySprites[category].Add(sprite);
            }

            bundle.Unload(false);
        }

        // 3. 排序
        foreach (var kvp in categorySprites)
        {
            kvp.Value.Sort((a, b) => string.Compare(a.name, b.name));
        }

        IsLoaded = true;
        Debug.Log($"AssetBundle 加载完成，共 {categorySprites.Count} 个分类");
    }

    public Sprite[] GetCategorySprites(string category)
    {
        if (categorySprites.TryGetValue(category, out var list))
            return list.ToArray();
        return new Sprite[0];
    }
}