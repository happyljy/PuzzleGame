using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 跨平台图片解码器。
/// 
/// 【为什么需要这个脚本？】
/// Unity 自带 Texture2D.LoadImage 只能解码 PNG / JPG / JPEG。
/// 但 Android 手机上用户可能选择 HEIC（iPhone 拍的）、WebP、GIF 等格式，
/// Unity 就解不出来。此时需要调用 Android 系统的原生解码器来帮忙。
/// 
/// 【本脚本做两件事】
/// 1. Android 平台：调用 Android 系统的 BitmapFactory 解码任意格式图片，
///    如果图片太大（超过 2048 像素）就等比缩小，最后压缩成 PNG 字节流返回。
///    这样返回给 Unity 的就是它能认的 PNG 格式。
/// 2. 其他平台（PC / Mac / 编辑器）：直接把原文件字节读出来，交给 Unity 处理。
/// 
/// 【为什么用 FileInputStream 而不是路径直读？】
/// Android 10（API 29）以后引入了"分区存储"（scoped storage），
/// App 不能直接访问其他 App 的文件路径。用 FileInputStream 打开文件，
/// 可以绕过这个限制，更可靠。
/// 
/// 【AndroidJavaObject / AndroidJavaClass 是什么？】
/// 这是 Unity 提供的"JNI 桥接"工具，让 C# 代码能调用 Android 的 Java 类。
/// - AndroidJavaClass  = Java 类的引用（类似 C# 里 typeof(某个类)）
/// - AndroidJavaObject = Java 对象的实例（类似 C# 里 new 出来的对象）
/// 比如 new AndroidJavaObject("java.io.File", path) 就相当于 Java 里 new File(path)。
/// </summary>
public static class AndroidImageDecoder
{
    /// <summary>
    /// 最近一次错误的描述信息。
    /// 外部代码可以在调用失败后读取这个属性，得知具体错误原因。
    /// </summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 解码后图片的最大边长（像素）。
    /// 如果原图超过这个尺寸，会等比缩小到这个值以内。
    /// 例：原图 4000×3000，MaxDecodeSize=2048，会缩成约 2048×1536。
    /// 这样可以避免内存爆炸，也加快后续处理。
    /// </summary>
    private const int MaxDecodeSize = 2048;

    #region 公共接口

    /// <summary>
    /// 把指定文件路径的图片解码为 PNG 字节数组。
    /// 
    /// 【使用场景】
    /// 用户从相册选择图片后，NativeGallery 会返回一个文件路径。
    /// 调用本方法可以把任意格式的图片转成 PNG 字节流，
    /// 之后再交给 Texture2D.LoadImage 使用。
    /// </summary>
    /// <param name="filePath">图片文件的完整路径</param>
    /// <returns>PNG 格式的字节数组；失败时返回 null，具体原因看 LastError</returns>
    public static byte[] DecodeToPngBytes(string filePath)
    {
        // 每次调用先清空上次的错误信息
        LastError = "";

        // 空路径直接报错
        if (string.IsNullOrEmpty(filePath))
        {
            LastError = "文件路径为空";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }

        // ---------- Android 真机（非编辑器）走原生解码 ----------
#if UNITY_ANDROID && !UNITY_EDITOR
        return DecodeFileViaStream(filePath);
#else
        // ---------- 其他平台直接读文件字节 ----------
        // 因为 PC / Mac / 编辑器环境下 Unity 能直接解码 PNG/JPG，
        // 不需要调用 Android 的 BitmapFactory。
        if (!File.Exists(filePath))
        {
            LastError = $"文件不存在: {filePath}";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }
        try
        {
            // 直接读全部字节返回
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

    /// <summary>
    /// 把 Android 的 content:// URI 解码为 PNG 字节数组。
    /// 
    /// 【什么是 content:// URI？】
    /// Android 从相册选图时，系统返回的不是文件路径，
    /// 而是类似 "content://media/external/images/media/1234" 的 URI。
    /// 需要借助 ContentResolver 才能读出图片内容。
    /// 
    /// 【使用场景】
    /// NativeGallery 返回以 "content://" 开头的字符串时用这个方法。
    /// </summary>
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
        // 非 Android 平台没这个概念，直接报错
        LastError = "非 Android 平台不支持 URI 解码";
        Debug.LogWarning($"[Decoder] {LastError}");
        return null;
#endif
    }

    #endregion

    // ---------- 下面的代码只在 Android 真机上编译 ----------
#if UNITY_ANDROID && !UNITY_EDITOR

    #region 文件解码（FileInputStream 方式）

    /// <summary>
    /// 通过 FileInputStream 打开文件路径，用 BitmapFactory 解码。
    /// 
    /// 【Android 端完整流程】
    /// 1. 创建 java.io.File 对象，检查文件是否存在、大小是否正常
    /// 2. 用 FileInputStream 打开文件
    /// 3. BitmapFactory.decodeStream() 解码成 Bitmap 对象
    /// 4. 调用 ProcessAndCompress 缩放并压缩成 PNG
    /// </summary>
    private static byte[] DecodeFileViaStream(string filePath)
    {
        // 声明 Java 对象引用（稍后创建）
        AndroidJavaObject file = null;        // java.io.File 实例
        AndroidJavaObject inputStream = null; // java.io.FileInputStream 实例
        AndroidJavaObject bitmap = null;      // android.graphics.Bitmap 实例

        try
        {
            // ============ 第 1 步：用 java.io.File 检查文件 ============
            // 相当于 Java 里：File file = new File(filePath);
            file = new AndroidJavaObject("java.io.File", filePath);

            // 相当于 Java 里：if (!file.exists()) { ... }
            if (!file.Call<bool>("exists"))
            {
                LastError = $"文件不存在: {filePath}";
                Debug.LogError($"[Decoder] {LastError}");
                return null;
            }

            // 相当于 Java 里：long fileLength = file.length();
            long fileLength = file.Call<long>("length");
            Debug.Log($"[Decoder] 文件大小: {fileLength} bytes");

            // 文件大小为 0 说明复制失败或文件损坏
            if (fileLength <= 0)
            {
                LastError = $"文件大小为 0，可能复制失败: {filePath}";
                Debug.LogError($"[Decoder] {LastError}");
                return null;
            }

            // ============ 第 2 步：打开文件输入流 ============
            // 相当于 Java 里：FileInputStream fis = new FileInputStream(file);
            inputStream = new AndroidJavaObject("java.io.FileInputStream", file);

            // ============ 第 3 步：用 BitmapFactory 解码 ============
            // AndroidJavaClass 用于访问 Java 类的静态方法。
            // using 会在代码块结束后自动释放 Java 侧的引用，避免内存泄漏。
            using (AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory"))
            {
                // 相当于 Java 里：Bitmap bmp = BitmapFactory.decodeStream(fis);
                // CallStatic 表示调用"静态方法"（不需要实例）
                bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeStream", inputStream);
            }

            // 解码后关闭输入流（good practice）
            inputStream.Call("close");

            // 如果 bitmap 是 null，说明解码失败（格式不支持 / 数据损坏）
            if (bitmap == null)
            {
                LastError = "decodeStream 返回 null，可能是格式不支持或数据损坏";
                Debug.LogError($"[Decoder] {LastError}");
                return null;
            }

            // ============ 第 4 步：缩放并压缩为 PNG ============
            return ProcessAndCompress(bitmap);
        }
        catch (Exception e)
        {
            // 任何 Java 调用异常都会被捕获
            LastError = $"文件解码异常: {e.Message}";
            Debug.LogError($"[Decoder] {LastError}\n{e.StackTrace}");
            return null;
        }
        finally
        {
            // ============ 清理资源 ============
            // 不管成功失败，都要释放 Java 侧的资源，避免内存泄漏。
            // Java 里的对象由 JVM 的 GC 管理，但 JNI 桥接会产生引用，
            // 需要显式 Dispose 让 Unity 释放这些引用。

            if (inputStream != null)
            {
                try { inputStream.Call("close"); } catch { } // 关流
                inputStream.Dispose();                        // 释放 JNI 引用
            }
            if (file != null) file.Dispose();
            if (bitmap != null)
            {
                try { bitmap.Call("recycle"); } catch { }     // 回收像素内存
                bitmap.Dispose();
            }
        }
    }

    #endregion

    #region URI 解码

    /// <summary>
    /// 用 ContentResolver 打开 URI 流并解码。
    /// 
    /// 【Android 端流程】
    /// 1. 拿到当前 Unity Activity
    /// 2. 从 Activity 拿到 ContentResolver（内容解析器）
    /// 3. 用 Uri.parse() 把字符串转成 Uri 对象
    /// 4. ContentResolver.openInputStream(uri) 打开输入流
    /// 5. BitmapFactory.decodeStream() 解码
    /// 6. 缩放压缩为 PNG
    /// </summary>
    private static byte[] DecodeUriSimple(string uriString)
    {
        AndroidJavaObject bitmap = null;

        try
        {
            // 用 using 一次性声明多个 Java 对象，它们会在代码块结束时自动 Dispose。
            // 相当于一个个嵌套的 try-finally。

            // UnityPlayer.currentActivity 是当前 Android 的 Activity（一个应用界面）
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            // 从 Activity 拿到 ContentResolver（用来访问内容提供者的工具）
            using (AndroidJavaObject contentResolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            // android.net.Uri 类
            using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
            // 相当于 Java 里：Uri uri = Uri.parse(uriString);
            using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", uriString))
            // BitmapFactory 类
            using (AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory"))
            // 相当于 Java 里：InputStream in = contentResolver.openInputStream(uri);
            using (AndroidJavaObject stream = contentResolver.Call<AndroidJavaObject>("openInputStream", uri))
            {
                // 打开失败
                if (stream == null)
                {
                    LastError = "无法打开输入流（openInputStream 返回 null）";
                    Debug.LogError($"[Decoder] {LastError}");
                    return null;
                }

                // 解码流
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

    /// <summary>
    /// 把解码后的 Bitmap 缩放（如需）并压缩为 PNG 字节数组。
    /// 
    /// 【为什么还要缩放？】
    /// 现在手机动辄 4000×3000 像素的照片，解码后占用约 48 MB 内存，
    /// 直接用作拼图纹理会导致卡顿甚至 OOM（内存溢出）。
    /// 缩到 2048 以内就够用，内存降低 4 倍以上。
    /// </summary>
    private static byte[] ProcessAndCompress(AndroidJavaObject bitmap)
    {
        // 相当于 Java 里：int w = bitmap.getWidth();
        int rawW = bitmap.Call<int>("getWidth");
        int rawH = bitmap.Call<int>("getHeight");
        Debug.Log($"[Decoder] 原始尺寸 {rawW}x{rawH}");

        // 尺寸异常直接报错
        if (rawW <= 0 || rawH <= 0)
        {
            LastError = $"Bitmap 尺寸异常 ({rawW}x{rawH})";
            Debug.LogError($"[Decoder] {LastError}");
            return null;
        }

        // 取长边判断是否需要缩放
        int longerSide = Math.Max(rawW, rawH);

        // 默认不缩放，直接压缩原始 bitmap
        AndroidJavaObject bitmapToCompress = bitmap;

        try
        {
            // 长边超过 2048 才缩放
            if (longerSide > MaxDecodeSize)
            {
                // 计算缩放比例，例：4000→2048，ratio=0.512
                float ratio = (float)MaxDecodeSize / longerSide;

                // 按比例算出新尺寸，至少为 1 像素
                int newW = Mathf.Max(1, Mathf.RoundToInt(rawW * ratio));
                int newH = Mathf.Max(1, Mathf.RoundToInt(rawH * ratio));

                Debug.Log($"[Decoder] 缩放到 {newW}x{newH}");

                using (AndroidJavaClass bitmapClass = new AndroidJavaClass("android.graphics.Bitmap"))
                {
                    // 相当于 Java 里：
                    // Bitmap scaled = Bitmap.createScaledBitmap(bmp, newW, newH, true);
                    // 最后一个参数 true 表示开启滤波（画质更平滑）
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

            // 压缩为 PNG 字节
            byte[] pngBytes = CompressBitmapToPng(bitmapToCompress);

            // PNG 结果至少 100 字节，太小说明有问题
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
            // 如果创建了新 bitmap（缩放过），要回收它，避免内存泄漏
            // 没缩放的话 bitmapToCompress 和 bitmap 是同一个，只回收一次
            if (bitmapToCompress != bitmap && bitmapToCompress != null)
            {
                try { bitmapToCompress.Call("recycle"); } catch { }
                bitmapToCompress.Dispose();
            }
        }
    }

    /// <summary>
    /// 把 Bitmap 对象压缩成 PNG 字节数组。
    /// 
    /// 【Android 端流程】
    /// 1. 创建一个 ByteArrayOutputStream 用来接收输出
    /// 2. 拿到 Bitmap.CompressFormat.PNG 枚举值
    /// 3. 调用 bitmap.compress(PNG, 100, outputStream)
    /// 4. 从输出流转成 byte[]
    /// </summary>
    private static byte[] CompressBitmapToPng(AndroidJavaObject bitmap)
    {
        if (bitmap == null) return null;

        try
        {
            // ByteArrayOutputStream 相当于内存里的一块缓冲区
            using (AndroidJavaObject outputStream = new AndroidJavaObject("java.io.ByteArrayOutputStream"))
            // Bitmap.CompressFormat 是一个 Java 内部枚举类
            // 名字里的 $ 是 Java 内部类的表示方式
            using (AndroidJavaClass compressFormatClass = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat"))
            {
                // 取 PNG 枚举值
                AndroidJavaObject pngFormat = compressFormatClass.GetStatic<AndroidJavaObject>("PNG");

                // 相当于 Java 里：bitmap.compress(PNG, 100, out);
                // 第二个参数 100 表示质量 100%（PNG 是无损格式，这个参数其实无效）
                bitmap.Call<bool>("compress", pngFormat, 100, outputStream);

                // 从输出流取出字节数组
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