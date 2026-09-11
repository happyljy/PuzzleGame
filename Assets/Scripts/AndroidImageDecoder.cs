using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 跨平台图片解码器：
/// - Android 平台：使用原生 BitmapFactory 解码任意格式（HEIC / WebP / GIF 等），
///   解码后如果尺寸过大，用 Bitmap.createScaledBitmap 缩放，最后压缩为 PNG 字节流返回。
/// - 其他平台：直接返回原文件字节，交给 Unity 自带解码器处理。
/// 
/// ★ 使用 FileInputStream 读取文件，绕过 Android 10+ scoped storage 对直接
///   文件路径访问的限制，同时打印文件大小用于排查。
/// </summary>
public static class AndroidImageDecoder
{
    public static string LastError { get; private set; } = "";

    private const int MaxDecodeSize = 2048;

    #region 公共接口

    public static byte[] DecodeToPngBytes(string filePath)
    {
        LastError = "";

        if (string.IsNullOrEmpty(filePath))
        {
            LastError = "文件路径为空";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        return DecodeFileViaStream(filePath);
#else
        if (!File.Exists(filePath))
        {
            LastError = $"文件不存在: {filePath}";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }
        try
        {
            return File.ReadAllBytes(filePath);
        }
        catch (Exception e)
        {
            LastError = $"读取文件异常: {e.Message}";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }
#endif
    }

    public static byte[] DecodeUriToPngBytes(string uriString)
    {
        LastError = "";

        if (string.IsNullOrEmpty(uriString))
        {
            LastError = "URI 字符串为空";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        return DecodeUriSimple(uriString);
#else
        LastError = "非 Android 平台不支持 URI 解码";
        Debug.LogWarning($"[Decoder] {LastError}");
        return null;
#endif
    }

    #endregion

#if UNITY_ANDROID && !UNITY_EDITOR

    #region 文件解码（FileInputStream 方式）

    private static byte[] DecodeFileViaStream(string filePath)
    {
        AndroidJavaObject file = null;
        AndroidJavaObject inputStream = null;
        AndroidJavaObject bitmap = null;

        try
        {
            // 1. 用 java.io.File 检查文件是否存在和大小
            file = new AndroidJavaObject("java.io.File", filePath);

            if (!file.Call<bool>("exists"))
            {
                LastError = $"文件不存在: {filePath}";
                Debug.LogError($"[Decoder] {LastError}");
                return null;
            }

            long fileLength = file.Call<long>("length");
            Debug.Log($"[Decoder] 文件大小: {fileLength} bytes");

            if (fileLength <= 0)
            {
                LastError = $"文件大小为 0，可能复制失败: {filePath}";
                Debug.LogError($"[Decoder] {LastError}");
                return null;
            }

            // 2. 用 FileInputStream 打开文件
            inputStream = new AndroidJavaObject("java.io.FileInputStream", file);

            // 3. 用 decodeStream 解码
            using (AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory"))
            {
                bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeStream", inputStream);
            }

            inputStream.Call("close");

            if (bitmap == null)
            {
                LastError = "decodeStream 返回 null，可能是格式不支持或数据损坏";
                Debug.LogError($"[Decoder] {LastError}");
                return null;
            }

            return ProcessAndCompress(bitmap);
        }
        catch (Exception e)
        {
            LastError = $"文件解码异常: {e.Message}";
            Debug.LogError($"[Decoder] {LastError}\n{e.StackTrace}");
            return null;
        }
        finally
        {
            if (inputStream != null)
            {
                try { inputStream.Call("close"); } catch { }
                inputStream.Dispose();
            }
            if (file != null) file.Dispose();
            if (bitmap != null)
            {
                try { bitmap.Call("recycle"); } catch { }
                bitmap.Dispose();
            }
        }
    }

    #endregion

    #region URI 解码

    private static byte[] DecodeUriSimple(string uriString)
    {
        AndroidJavaObject bitmap = null;

        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject contentResolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
            using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", uriString))
            using (AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory"))
            using (AndroidJavaObject stream = contentResolver.Call<AndroidJavaObject>("openInputStream", uri))
            {
                if (stream == null)
                {
                    LastError = "无法打开输入流（openInputStream 返回 null）";
                    Debug.LogError($"[Decoder] {LastError}");
                    return null;
                }

                bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeStream", stream);
                stream.Call("close");

                if (bitmap == null)
                {
                    LastError = "decodeStream 返回 null，可能是格式不支持或数据损坏";
                    Debug.LogError($"[Decoder] {LastError}");
                    return null;
                }

                return ProcessAndCompress(bitmap);
            }
        }
        catch (Exception e)
        {
            LastError = $"URI 解码异常: {e.Message}";
            Debug.LogError($"[Decoder] {LastError}\n{e.StackTrace}");
            return null;
        }
        finally
        {
            if (bitmap != null)
            {
                try { bitmap.Call("recycle"); } catch { }
                bitmap.Dispose();
            }
        }
    }

    #endregion

    #region 缩放 + 压缩

    private static byte[] ProcessAndCompress(AndroidJavaObject bitmap)
    {
        int rawW = bitmap.Call<int>("getWidth");
        int rawH = bitmap.Call<int>("getHeight");
        Debug.Log($"[Decoder] 原始尺寸 {rawW}x{rawH}");

        if (rawW <= 0 || rawH <= 0)
        {
            LastError = $"Bitmap 尺寸异常 ({rawW}x{rawH})";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }

        int longerSide = Math.Max(rawW, rawH);
        AndroidJavaObject bitmapToCompress = bitmap;

        try
        {
            if (longerSide > MaxDecodeSize)
            {
                float ratio = (float)MaxDecodeSize / longerSide;
                int newW = Mathf.Max(1, Mathf.RoundToInt(rawW * ratio));
                int newH = Mathf.Max(1, Mathf.RoundToInt(rawH * ratio));

                Debug.Log($"[Decoder] 缩放到 {newW}x{newH}");

                using (AndroidJavaClass bitmapClass = new AndroidJavaClass("android.graphics.Bitmap"))
                {
                    bitmapToCompress = bitmapClass.CallStatic<AndroidJavaObject>(
                        "createScaledBitmap", bitmap, newW, newH, true);
                }

                if (bitmapToCompress == null)
                {
                    LastError = "createScaledBitmap 返回 null";
                    Debug.LogError($"[Decoder] {LastError}");
                    return null;
                }
            }

            byte[] pngBytes = CompressBitmapToPng(bitmapToCompress);

            if (pngBytes == null || pngBytes.Length < 100)
            {
                LastError = $"PNG 压缩结果异常 ({(pngBytes == null ? 0 : pngBytes.Length)} bytes)";
                Debug.LogError($"[Decoder] {LastError}");
                return null;
            }

            Debug.Log($"[Decoder] 解码完成，{pngBytes.Length} bytes");
            return pngBytes;
        }
        finally
        {
            if (bitmapToCompress != bitmap && bitmapToCompress != null)
            {
                try { bitmapToCompress.Call("recycle"); } catch { }
                bitmapToCompress.Dispose();
            }
        }
    }

    private static byte[] CompressBitmapToPng(AndroidJavaObject bitmap)
    {
        if (bitmap == null) return null;

        try
        {
            using (AndroidJavaObject outputStream = new AndroidJavaObject("java.io.ByteArrayOutputStream"))
            using (AndroidJavaClass compressFormatClass = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat"))
            {
                AndroidJavaObject pngFormat = compressFormatClass.GetStatic<AndroidJavaObject>("PNG");
                bitmap.Call<bool>("compress", pngFormat, 100, outputStream);
                byte[] bytes = outputStream.Call<byte[]>("toByteArray");
                outputStream.Call("close");
                return bytes;
            }
        }
        catch (Exception e)
        {
            LastError = $"PNG 压缩异常: {e.Message}";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }
    }

    #endregion

#endif
}