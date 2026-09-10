using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildAssetBundles
{
    [MenuItem("Tools/Build AssetBundles")]
    static void Build()
    {
        string outputPath = Path.Combine(Application.streamingAssetsPath, "AssetBundles");
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        BuildPipeline.BuildAssetBundles(outputPath,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.Android);

        Debug.Log("AssetBundle 构建完成: " + outputPath);
        // 打开输出文件夹方便查看
        EditorUtility.RevealInFinder(outputPath);
    }
}