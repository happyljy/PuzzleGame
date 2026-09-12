using System;                    // Action 委托、Exception
using System.Collections;        // IEnumerator（协程）
using System.IO;                 // File、Path、FileInfo
using UnityEngine;               // Unity 基础 API

/// <summary>
/// 图片加载工具（静态类，不需要挂载到物体上）。
///
/// 【这个脚本干什么？】
/// 从本地文件路径加载一张图片，变成可以在 UI 里显示的 Sprite。
/// 这是游戏里"上传图片 / 共享图片"功能的核心。
///
/// 【为什么要搞这么复杂？】
/// Unity 的 Texture2D.LoadImage 只支持 PNG / JPG / JPEG，
/// 而 Android 手机上的相册可能有 HEIC（iPhone 拍的）、WebP 等格式。
/// 所以脚本要根据扩展名分流：
///   - PNG / JPG / JPEG → Unity 直接读文件
///   - Android 其他格式  → 调用 AndroidImageDecoder 原生解码后再转 PNG
///
/// 【为什么要异步（协程 + 后台线程）？】
/// 读文件和图片解码都是"耗时操作"（几十毫秒到几秒）。
/// 如果放在主线程，UI 会卡住，用户觉得游戏卡死。
/// 所以：
///   1. 文件读取 + 格式转换 → 后台线程（Task.Run）
///   2. Texture2D / Sprite 创建 → 必须主线程（Unity 规定）
/// 本脚本用"协程 + 后台线程"组合实现异步。
/// </summary>
public static class ImageLoader
{
    /// <summary>
    /// 图片的最大边长（像素）。
    /// 超过这个尺寸的图片会被等比缩小，避免占用过多显存。
    /// 2048×2048 的 RGBA32 纹理约 16 MB，是手机的合理上限。
    /// </summary>
    private const int MaxTextureSize = 2048;

    #region 同步加载（少用）

    /// <summary>
    /// 同步加载 Sprite。
    /// 
    /// 【注意】
    /// 这个方法会阻塞调用它的线程。
    /// 如果在主线程调用，游戏会卡住直到加载完成。
    /// 一般只在编辑器脚本或测试时使用，游戏运行时请用异步版本。
    /// </summary>
    public static Sprite LoadSpriteFromFile(string path)
    {
        // 读文件的字节数据
        byte[] bytes = ReadImageBytes(path);

        // 读取失败
        if (bytes == null || bytes.Length == 0)
            return null;

        // 从字节创建 Sprite
        return CreateSpriteFromImageBytes(bytes);
    }

    #endregion

    #region 异步加载（推荐）

    /// <summary>
    /// 异步加载 Sprite。
    /// 文件读取和特殊格式转换放后台线程。
    /// Texture2D / Sprite 的创建始终在主线程。
    ///
    /// 【使用方式】
    /// <code>
    /// Sprite result = null;
    /// yield return ImageLoader.LoadSpriteFromFileAsync(path, (s) => result = s);
    /// // 到这里 result 就是加载好的 Sprite（可能是 null 表示失败）
    /// </code>
    ///
    /// 【返回 IEnumerator 是什么？】
    /// 表示这是一个"协程"，可以被 yield return 暂停和恢复。
    /// 调用方用 yield return 等它完成，就能拿到加载结果。
    /// </summary>
    public static IEnumerator LoadSpriteFromFileAsync(
        string path,
        Action<Sprite> onLoaded)
    {
        byte[] imageBytes = null;
        bool finished = false;   // 后台线程完成的标志

        Debug.Log($"[ImageLoader] 开始加载: {path}");

        // ========== 第 1 步：在后台线程读文件字节 ==========
        // Task.Run 会把委托放到线程池线程执行，不阻塞主线程。
        // 这就是"异步"的关键：
        //   主线程继续跑游戏，后台线程默默读文件。
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                // 读文件（可能是 PNG 直读，也可能是 Android 特殊格式转换）
                imageBytes = ReadImageBytes(path);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[ImageLoader] 后台读取异常: {e.Message}\n{e.StackTrace}");
                imageBytes = null;
            }
            finally
            {
                // 不管成功失败，都要标记完成
                finished = true;
            }
        });

        // ========== 第 2 步：主线程轮询等待 ==========
        // 用一个 while 循环 + yield return null 每帧检查一次。
        // 这样不会阻塞主线程（yield 会让出），但也不会立刻继续，
        // 而是等后台线程把 finished 设成 true。
        while (!finished)
            yield return null;

        // ========== 第 3 步：检查读取结果 ==========
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogError($"[ImageLoader] 读取图片失败: {path}");
            onLoaded?.Invoke(null);   // 通知调用方失败
            yield break;              // 结束协程
        }

        Debug.Log(
            $"[ImageLoader] 图片读取成功: {imageBytes.Length} bytes");

        // ==================================================
        // 重要：
        // Texture2D.LoadImage 必须在 Unity 主线程执行。
        // 所以这一步不能再放后台线程里。
        // ==================================================

        Sprite sprite = CreateSpriteFromImageBytes(imageBytes);

        if (sprite == null)
        {
            Debug.LogError(
                $"[ImageLoader] 创建 Sprite 失败: {path}");
        }
        else
        {
            Debug.Log(
                $"[ImageLoader] Sprite 创建成功: " +
                $"{sprite.texture.width}x{sprite.texture.height}");
        }

        // 通过回调把结果交给调用方
        onLoaded?.Invoke(sprite);
    }

    #endregion

    #region 文件字节读取

    /// <summary>
    /// 根据文件格式读取图片字节。
    /// 对 PNG/JPG/JPEG 等 Unity 原生支持格式，直接读取文件。
    /// Android 特殊格式才使用 AndroidImageDecoder。
    ///
    /// 【为什么要区分格式？】
    /// Unity 的 Texture2D.LoadImage 能直接解码 PNG/JPG。
    /// 但 HEIC/WebP 之类的格式解不了，必须用 Android 原生解码器。
    /// 直接读 PNG/JPG 比走 Android 解码快很多，所以要做分流。
    /// </summary>
    private static byte[] ReadImageBytes(string path)
    {
        // ---------- 参数检查 ----------
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[ImageLoader] path 为空");
            return null;
        }

        if (!File.Exists(path))
        {
            Debug.LogError(
                $"[ImageLoader] 文件不存在: {path}");
            return null;
        }

        // 获取文件大小（用于诊断）
        FileInfo fileInfo = new FileInfo(path);

        Debug.Log(
            $"[ImageLoader] 文件存在: {path}, " +
            $"size={fileInfo.Length}");

        if (fileInfo.Length <= 0)
        {
            Debug.LogError("[ImageLoader] 文件大小为 0");
            return null;
        }

        // ---------- 获取扩展名（统一小写） ----------
        // Path.GetExtension("abc.PNG") 返回 ".PNG"
        // ToLowerInvariant() 转小写，避免大小写不一致的问题
        // 例："photo.JPG" → ".jpg"
        string extension =
            Path.GetExtension(path).ToLowerInvariant();

        // ==================================================
        // 情况 1：PNG / JPG / JPEG —— Unity 原生支持
        // 这些格式 Unity Texture2D.LoadImage 本身可以处理，
        // 不需要 Android BitmapFactory，直接读文件最省事。
        // ==================================================
        if (extension == ".png" ||
            extension == ".jpg" ||
            extension == ".jpeg")
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);

                Debug.Log(
                    $"[ImageLoader] Unity 原生格式读取成功: " +
                    $"{extension}, {bytes.Length} bytes");

                return bytes;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[ImageLoader] File.ReadAllBytes 失败: " +
                    $"{e.Message}");

                return null;
            }
        }

        // ---------- 下面的代码只在 Android 真机上编译 ----------
#if UNITY_ANDROID && !UNITY_EDITOR

        // ==================================================
        // 情况 2：Android 特殊格式（HEIC / WebP / GIF 等）
        // 调用 AndroidImageDecoder 原生解码，
        // 它会返回转换后的 PNG 字节流。
        // ==================================================
        Debug.Log(
            $"[ImageLoader] 特殊格式，调用 AndroidImageDecoder: {extension}");

        byte[] decodedBytes =
            AndroidImageDecoder.DecodeToPngBytes(path);

        if (decodedBytes == null ||
            decodedBytes.Length == 0)
        {
            Debug.LogError(
                $"[ImageLoader] AndroidImageDecoder 失败: " +
                $"{AndroidImageDecoder.LastError}");

            return null;
        }

        Debug.Log(
            $"[ImageLoader] Android 特殊格式转换成功: " +
            $"{decodedBytes.Length} bytes");

        return decodedBytes;

#else

        // ==================================================
        // 情况 3：非 Android 平台（PC / Mac / 编辑器）
        // 直接读文件字节，交给 Unity 处理。
        // ==================================================
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"[ImageLoader] File.ReadAllBytes 失败: {e.Message}");

            return null;
        }

#endif
    }

    #endregion

    #region 创建 Sprite

    /// <summary>
    /// 主线程创建 Texture2D + Sprite。
    ///
    /// 【为什么要先创建 Texture2D 再创建 Sprite？】
    /// - Texture2D 是"图片数据本身"（像素、宽高）
    /// - Sprite 是"包装类"，告诉 Unity 这块纹理的哪一部分要显示
    /// Sprite.Create 依赖 Texture2D，所以必须两步走。
    ///
    /// 【流程】
    ///   1. new Texture2D —— 创建一个空纹理
    ///   2. tex.LoadImage(bytes) —— 把字节解码进纹理
    ///   3. 如果太大，ResizeTexture 缩小
    ///   4. Sprite.Create —— 用纹理创建 Sprite
    /// </summary>
    private static Sprite CreateSpriteFromImageBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            Debug.LogError(
                "[ImageLoader] CreateSpriteFromImageBytes: bytes 为空");

            return null;
        }

        Texture2D tex = null;

        try
        {
            // ---------- 第 1 步：创建空纹理 ----------
            // 参数含义：
            //   2, 2          → 初始尺寸（会被 LoadImage 覆盖，随便填）
            //   RGBA32        → 像素格式（32 位，含透明度，通用性最好）
            //   false         → 不用 mipmap（UI 用不到，省内存）
            tex = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);

            Debug.Log(
                $"[ImageLoader] 开始 Texture2D.LoadImage: " +
                $"{bytes.Length} bytes");

            // ---------- 第 2 步：解码字节到纹理 ----------
            // LoadImage 会自动识别字节是 PNG 还是 JPG 并解码。
            // 第二个参数 false 表示不生成 mipmap（省内存）。
            bool loaded = tex.LoadImage(bytes, false);

            if (!loaded)
            {
                Debug.LogError(
                    "[ImageLoader] Texture2D.LoadImage 返回 false");

                UnityEngine.Object.Destroy(tex);
                return null;
            }

            Debug.Log(
                $"[ImageLoader] LoadImage 成功: " +
                $"{tex.width}x{tex.height}");

            // 尺寸异常检查（理论上不会发生，但保险起见）
            if (tex.width <= 0 || tex.height <= 0)
            {
                Debug.LogError(
                    $"[ImageLoader] Texture 尺寸异常: " +
                    $"{tex.width}x{tex.height}");

                UnityEngine.Object.Destroy(tex);
                return null;
            }

            // ==================================================
            // 第 3 步：尺寸保护
            // 如果图片尺寸太大，缩到 2048 以内。
            // 手机显存有限，一张 4000×4000 的图片可能直接 OOM 崩溃。
            // ==================================================
            if (tex.width > MaxTextureSize ||
                tex.height > MaxTextureSize)
            {
                Debug.Log(
                    $"[ImageLoader] Texture 尺寸超过 {MaxTextureSize}，开始缩放");

                Texture2D resized =
                    ResizeTexture(tex, MaxTextureSize);

                // 缩放失败就直接退出（ResizeTexture 内部已经 Destroy 旧 tex）
                if (resized == null)
                    return null;

                // 用缩放后的纹理替换原纹理
                // 注意：原来那个 tex 已经在 ResizeTexture 里被 Destroy 了
                tex = resized;
            }

            // ---------- 第 4 步：应用纹理变更 ----------
            // Apply 的参数：
            //   updateMipmaps = false → 不重新生成 mipmap
            //   makeNoLongerReadable = false → 保留 CPU 可读副本
            tex.Apply(false, false);

            // ---------- 第 5 步：创建 Sprite ----------
            // Sprite.Create 参数含义：
            //   texture         → 用哪个纹理
            //   rect            → 用纹理的哪一块（这里是整张）
            //   pivot           → 轴心位置（(0.5, 0.5) 表示中心）
            //   pixelsPerUnit   → 每单位多少像素（100 是标准值）
            //   extrude         → 边缘挤出（0 表示不挤出）
            //   meshType        → 网格类型（FullRect 用矩形网格）
            Sprite sprite = Sprite.Create(
                tex,
                new Rect(
                    0,
                    0,
                    tex.width,
                    tex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

            if (sprite == null)
            {
                Debug.LogError(
                    "[ImageLoader] Sprite.Create 返回 null");

                UnityEngine.Object.Destroy(tex);
                return null;
            }

            return sprite;
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"[ImageLoader] 创建 Sprite 异常: " +
                $"{e.Message}\n{e.StackTrace}");

            // 出错时清理已创建的纹理，避免内存泄漏
            if (tex != null)
                UnityEngine.Object.Destroy(tex);

            return null;
        }
    }

    #endregion

    #region 纹理缩放

    /// <summary>
    /// 按比例缩放 Texture。
    ///
    /// 【为什么不用 Texture2D.Resize？】
    /// Unity 没有提供简单的 Resize 方法，需要借助 RenderTexture 来实现。
    ///
    /// 【核心流程】
    ///   1. 创建一个 RenderTexture（临时渲染目标）
    ///   2. 用 Graphics.Blit 把原纹理"画"到 RenderTexture 上
    ///      —— 这一步 GPU 会自动做缩放
    ///   3. 从 RenderTexture 读像素到新 Texture2D
    ///   4. 释放临时资源
    /// </summary>
    private static Texture2D ResizeTexture(
        Texture2D source,
        int maxSize)
    {
        if (source == null)
            return null;

        // ---------- 第 1 步：计算缩放比例 ----------
        // 取"宽高各自能缩到 maxSize 的比例"中较小的那个，
        // 保证宽和高都不超过 maxSize。
        // 例：源 4000×3000，maxSize=2048
        //   width 比例  = 2048 / 4000 = 0.512
        //   height 比例 = 2048 / 3000 = 0.683
        //   Mathf.Min 取 0.512，保证宽和高都不超过 2048
        float ratio = Mathf.Min(
            (float)maxSize / source.width,
            (float)maxSize / source.height);

        // 算出新的宽高（Mathf.Max 保证至少为 1 像素）
        int newW = Mathf.Max(
            1,
            Mathf.RoundToInt(source.width * ratio));

        int newH = Mathf.Max(
            1,
            Mathf.RoundToInt(source.height * ratio));

        Debug.Log(
            $"[ImageLoader] ResizeTexture: " +
            $"{source.width}x{source.height} -> " +
            $"{newW}x{newH}");

        // ---------- 第 2 步：创建新的空纹理 ----------
        Texture2D resized =
            new Texture2D(
                newW,
                newH,
                TextureFormat.RGBA32,
                false);

        // ---------- 第 3 步：创建临时 RenderTexture ----------
        // RenderTexture.GetTemporary 会从对象池里取一个可用的，
        // 比直接 new 更高效（避免频繁分配释放）。
        RenderTexture rt =
            RenderTexture.GetTemporary(
                newW,
                newH);

        // 保存当前激活的 RenderTexture，稍后要恢复
        RenderTexture previous =
            RenderTexture.active;

        try
        {
            // ---------- 第 4 步：用 GPU 缩放 ----------
            // Graphics.Blit 把源纹理"画"到目标 RenderTexture 上，
            // GPU 会自动做缩放（双线性插值），速度比 CPU 快很多。
            Graphics.Blit(source, rt);

            // 设置 rt 为当前激活的渲染目标，
            // 这样 ReadPixels 才会从 rt 读取
            RenderTexture.active = rt;

            // ---------- 第 5 步：从 RenderTexture 读像素到新纹理 ----------
            resized.ReadPixels(
                new Rect(
                    0,
                    0,
                    newW,
                    newH),
                0,      // 目标纹理的起始 x
                0);     // 目标纹理的起始 y

            // 应用变更
            resized.Apply(false, false);
        }
        finally
        {
            // ---------- 第 6 步：清理 ----------
            // 恢复原来的激活 RenderTexture
            RenderTexture.active = previous;

            // 归还 RenderTexture 到对象池
            RenderTexture.ReleaseTemporary(rt);
        }

        // 销毁源纹理（它已经没用了）
        UnityEngine.Object.Destroy(source);

        return resized;
    }

    #endregion
}