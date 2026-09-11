using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// 图片加载工具。
///
/// PNG/JPG 等 Unity 原生支持的格式直接读取字节并由 Texture2D.LoadImage 解码。
/// Android 上仅对 Unity 无法可靠处理的特殊格式（例如 HEIC/WebP）使用原生解码器。
/// </summary>
public static class ImageLoader
{
    private const int MaxTextureSize = 2048;

    /// <summary>
    /// 同步加载 Sprite。
    /// </summary>
    public static Sprite LoadSpriteFromFile(string path)
    {
        byte[] bytes = ReadImageBytes(path);

        if (bytes == null || bytes.Length == 0)
            return null;

        return CreateSpriteFromImageBytes(bytes);
    }

    /// <summary>
    /// 异步加载 Sprite。
    /// 文件读取和特殊格式转换放后台线程。
    /// Texture2D / Sprite 的创建始终在主线程。
    /// </summary>
    public static IEnumerator LoadSpriteFromFileAsync(
        string path,
        Action<Sprite> onLoaded)
    {
        byte[] imageBytes = null;
        bool finished = false;

        Debug.Log($"[ImageLoader] 开始加载: {path}");

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
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
                finished = true;
            }
        });

        while (!finished)
            yield return null;

        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogError($"[ImageLoader] 读取图片失败: {path}");
            onLoaded?.Invoke(null);
            yield break;
        }

        Debug.Log(
            $"[ImageLoader] 图片读取成功: {imageBytes.Length} bytes");

        // ==================================================
        // 重要：
        // Texture2D.LoadImage 必须在 Unity 主线程执行
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

        onLoaded?.Invoke(sprite);
    }

    /// <summary>
    /// 根据文件格式读取图片。
    /// 对 PNG/JPG/JPEG 等 Unity 原生支持格式，直接读取文件。
    /// Android 特殊格式才使用 AndroidImageDecoder。
    /// </summary>
    private static byte[] ReadImageBytes(string path)
    {
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

        FileInfo fileInfo = new FileInfo(path);

        Debug.Log(
            $"[ImageLoader] 文件存在: {path}, " +
            $"size={fileInfo.Length}");

        if (fileInfo.Length <= 0)
        {
            Debug.LogError("[ImageLoader] 文件大小为 0");
            return null;
        }

        string extension =
            Path.GetExtension(path).ToLowerInvariant();

        // ==================================================
        // PNG / JPG / JPEG
        //
        // 这些格式 Unity Texture2D.LoadImage 本身可以处理。
        // 不需要 Android BitmapFactory。
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

#if UNITY_ANDROID && !UNITY_EDITOR

        // ==================================================
        // Android 特殊格式
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

        // 非 Android 平台直接读取
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

    /// <summary>
    /// 主线程创建 Texture2D + Sprite。
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
            tex = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);

            Debug.Log(
                $"[ImageLoader] 开始 Texture2D.LoadImage: " +
                $"{bytes.Length} bytes");

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

            if (tex.width <= 0 || tex.height <= 0)
            {
                Debug.LogError(
                    $"[ImageLoader] Texture 尺寸异常: " +
                    $"{tex.width}x{tex.height}");

                UnityEngine.Object.Destroy(tex);
                return null;
            }

            // ==================================================
            // 尺寸保护
            // ==================================================

            if (tex.width > MaxTextureSize ||
                tex.height > MaxTextureSize)
            {
                Debug.Log(
                    $"[ImageLoader] Texture 尺寸超过 {MaxTextureSize}，开始缩放");

                Texture2D resized =
                    ResizeTexture(tex, MaxTextureSize);

                if (resized == null)
                    return null;

                tex = resized;
            }

            tex.Apply(false, false);

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

            if (tex != null)
                UnityEngine.Object.Destroy(tex);

            return null;
        }
    }

    /// <summary>
    /// 按比例缩放 Texture。
    /// </summary>
    private static Texture2D ResizeTexture(
        Texture2D source,
        int maxSize)
    {
        if (source == null)
            return null;

        float ratio = Mathf.Min(
            (float)maxSize / source.width,
            (float)maxSize / source.height);

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

        Texture2D resized =
            new Texture2D(
                newW,
                newH,
                TextureFormat.RGBA32,
                false);

        RenderTexture rt =
            RenderTexture.GetTemporary(
                newW,
                newH);

        RenderTexture previous =
            RenderTexture.active;

        try
        {
            Graphics.Blit(source, rt);

            RenderTexture.active = rt;

            resized.ReadPixels(
                new Rect(
                    0,
                    0,
                    newW,
                    newH),
                0,
                0);

            resized.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;

            RenderTexture.ReleaseTemporary(rt);
        }

        UnityEngine.Object.Destroy(source);

        return resized;
    }
}