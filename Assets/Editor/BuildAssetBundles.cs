using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// AssetBundle 构建工具（仅编辑器使用）。
/// 通过菜单 Tools → Build AssetBundles 触发构建，
/// 生成的 AB 包会输出到 Assets/StreamingAssets/AssetBundles 目录下，
/// 随 APK 一起打包。
/// </summary>
public static class BuildAssetBundles
{
    #region 常量

    /// <summary>AssetBundle 输出目录（相对于 StreamingAssets）。</summary>
    private const string OutputFolderName = "AssetBundles";

    #endregion

    #region 菜单项

    /// <summary>
    /// 构建 AssetBundle 到 StreamingAssets/AssetBundles。
    /// 使用 LZ4 压缩（ChunkBasedCompression），目标平台为 Android。
    /// </summary>
    [MenuItem("Tools/Build AssetBundles")]
    public static void Build()
    {
        // 1. 确保输出目录存在
        string outputPath = Path.Combine(Application.streamingAssetsPath, OutputFolderName);
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        // 2. 执行构建
        BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.Android);

        // 3. 输出日志并打开文件夹
        Debug.Log($"AssetBundle 构建完成: {outputPath}");
        EditorUtility.RevealInFinder(outputPath);
    }

    #endregion
}