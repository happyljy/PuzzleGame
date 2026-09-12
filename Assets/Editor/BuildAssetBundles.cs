using UnityEditor;        // Unity 编辑器相关 API（BuildPipeline、EditorUtility 等）
using UnityEngine;        // Unity 通用 API（Application、Debug 等）
using System.IO;          // .NET 文件/目录操作（Path、Directory）

/// <summary>
/// AssetBundle 构建工具（仅编辑器使用）。
///
/// 【这个脚本干什么？】
/// 把项目里设置了 AssetBundle 标签的资源，打包成 .ab 文件，
/// 输出到 Assets/StreamingAssets/AssetBundles 文件夹里。
/// 打包 APK 时这些文件会自动被塞进去，游戏运行时就能加载。
///
/// 【为什么必须放在 Editor 文件夹里？】
/// 因为用到了 UnityEditor 命名空间的 API（比如 BuildPipeline）。
/// Unity 规定：只有放在名为 "Editor" 的文件夹里的脚本，
/// 才会在打包游戏时被"排除"，不会进入最终 APK。
/// 否则会报"UnityEditor 找不到"的编译错误。
///
/// 【怎么用？】
/// Unity 顶部菜单栏 → Tools → Build AssetBundles
/// 点一下就会自动打包，并打开输出文件夹。
/// </summary>
public static class BuildAssetBundles
{
    #region 常量

    /// <summary>
    /// AssetBundle 输出目录名（相对于 StreamingAssets）。
    /// 最终输出路径 = Application.streamingAssetsPath + "/" + "AssetBundles"
    /// 例：D:/MyProject/Assets/StreamingAssets/AssetBundles
    /// </summary>
    private const string OutputFolderName = "AssetBundles";

    #endregion

    #region 菜单项

    /// <summary>
    /// 构建 AssetBundle 到 StreamingAssets/AssetBundles。
    /// 使用 LZ4 压缩（ChunkBasedCompression），目标平台为 Android。
    ///
    /// 【[MenuItem] 特性是干嘛的？】
    /// 它会在 Unity 顶部菜单栏生成一个菜单项。
    /// "Tools/Build AssetBundles" 表示：
    ///   顶级菜单名 = Tools
    ///   子菜单名   = Build AssetBundles
    /// 点击菜单项，Unity 就会自动调用下面这个静态方法 Build()。
    /// 只有静态方法才能被 MenuItem 使用。
    /// </summary>
    [MenuItem("Tools/Build AssetBundles")]
    public static void Build()
    {
        // ---------- 第 1 步：确保输出目录存在 ----------
        // Path.Combine 会按平台自动用 / 或 \ 拼接路径。
        // 例：Windows 下 → D:\MyProject\Assets\StreamingAssets\AssetBundles
        //     Mac 下    → /Users/xxx/MyProject/Assets/StreamingAssets/AssetBundles
        string outputPath = Path.Combine(Application.streamingAssetsPath, OutputFolderName);

        // 如果目录不存在就创建（CreateDirectory 会自动创建中间目录）
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        // ---------- 第 2 步：执行 AB 构建 ----------
        // BuildPipeline.BuildAssetBundles 是 Unity 提供的核心打包方法。
        // 参数 1：输出目录
        // 参数 2：打包选项（下面详解）
        // 参数 3：目标平台
        BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.ChunkBasedCompression,  // LZ4 压缩
            BuildTarget.Android);                            // 目标平台：Android

        // ---------- 第 3 步：输出日志并打开文件夹 ----------
        Debug.Log($"AssetBundle 构建完成: {outputPath}");

        // RevealInFinder 会在系统文件管理器里打开该文件夹
        // Windows = 资源管理器，Mac = Finder
        // 注意：在 Mac 上 RevealInFinder 一样可用（Unity 内部会适配）
        EditorUtility.RevealInFinder(outputPath);
    }

    #endregion
}