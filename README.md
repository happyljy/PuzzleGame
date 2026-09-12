# 🧩 PuzzleGame

一个基于 Unity 的图片拼图 Android 手机游戏，支持多分类图片、多种难度、图片上传、局域网图片分享与拼图进度保存。

> ℹ️ **本项目为功能演示 Demo**，主要用于展示 Unity 拼图与局域网传输的实现思路。

---

## 📖 项目简介

PuzzleGame 是一款休闲拼图手机游戏，玩家从图库中选择图片，将其切割为 N×N 的碎片，通过拖拽和旋转完成拼图。项目支持：

- **6 个内置图片分类**：Kazimierz、Kjerag、RhodesIsland、Ursus、Victoria、Yan
- **3 种难度**：简单 (2×2)、普通 (8×8)、困难 (10×10)
- **上传图片**：从设备相册选择图片并加入拼图图库
- **局域网分享**：同一 Wi-Fi 下，设备间互相传输图片
- **拼图进度保存**：未完成的拼图自动保存已锁定碎片，下次进入可继续
- **每日拼图**：每天随机生成 10 张拼图任务，全部完成获得额外奖励
- **收藏系统**：收藏喜欢的图片，快速访问
- **体力系统**：拼图消耗体力，体力随时间恢复，也可通过看广告获得
- **金币与等级**：完成任务获得金币和经验，金币可解锁更多图片
- **头像与改名**：自定义玩家头像和昵称

---

## 🛠️ 环境要求

| 项目 | 版本 |
|------|------|
| Unity | 6000.2.1f1 |
| JDK | 17.0.20.8-hotspot |
| Android NDK | r27c (27.2.12479018) |
| Android SDK | Android 15.0 (Vanilla Ice Cream) API Level 35 |
| 最低 API 等级 | Android 6.0 Marshmallow (API 23) |
| 目标 API 等级 | Android 15.0 (API 35) |
| API 兼容性等级 | .NET Standard 2.1 |
| Active Input Handling | Both（Input Manager + Input System） |

### 第三方依赖

| 插件 | 版本 | 用途 |
|------|------|------|
| [Native Gallery for Android & iOS](https://github.com/yasirkula/UnityNativeGallery) | 1.9.4 | 从设备相册选择图片 |

> **注意**：
> - Unity 6000.0+ 版本需要 JDK 17 和 NDK r27c。
> - `Active Input Handling` 必须选 **Both**，否则拖拽/缩放等触摸操作会失效。

---

## 📁 项目结构

```
Assets/
├── Scripts/
│   ├── MainMenuManager.cs          # 主菜单管理器
│   ├── GameManager.cs              # 拼图游戏主逻辑
│   ├── PuzzlePiece.cs              # 拼图碎片交互
│   ├── GameDataManager.cs          # 全局数据持久化
│   ├── LANShareManager.cs          # 局域网分享（TCP/UDP）
│   ├── AssetBundleManager.cs       # AssetBundle 加载与分类
│   ├── ImageLoader.cs              # 图片异步加载
│   ├── AndroidImageDecoder.cs      # Android 原生解码
│   ├── SoundManager.cs             # 全局音频管理
│   ├── UnityMainThreadDispatcher.cs# 主线程调度器
│   ├── LoadingDots.cs              # 加载动画
│   ├── SafeArea.cs                 # 刘海屏适配
│   ├── SplashAnimation.cs          # 开场动画
│   ├── ButtonClickSound.cs         # 按钮音效
│   └── TextBreathing.cs            # 文字呼吸效果
├── Editor/
│   └── BuildAssetBundles.cs        # AssetBundle 构建工具
├── PuzzleArt/                      # 拼图图片源目录（打包为 AssetBundle）
│   ├── Kazimierz/                  # 分类文件夹
│   │   ├── Kazimierz_0.png         # 命名格式：类名_序号
│   │   ├── Kazimierz_1.png
│   │   └── ...
│   ├── Kjerag/
│   ├── RhodesIsland/
│   ├── Ursus/
│   ├── Victoria/
│   └── Yan/
├── Resources/
│   └── Art/
│       └── HeadPicture/            # 头像资源
├── StreamingAssets/
│   └── AssetBundles/               # 构建后的 AB 包
├── Scenes/
│   ├── SplashScene.unity           # 开场动画场景
│   ├── LevelScene.unity            # 主菜单场景
│   └── GameScene.unity             # 拼图游戏场景
└── ...
```

---

## 🖼️ 图片资源规范

### 目录组织

拼图图片存放在 `Assets/PuzzleArt/` 目录下，每个分类一个子文件夹：

```
Assets/PuzzleArt/
├── Kazimierz/
├── Kjerag/
├── RhodesIsland/
├── Ursus/
├── Victoria/
└── Yan/
```

### 命名规范

图片文件名必须遵循 **`类名_序号`** 格式，例如：

```
Kazimierz_0.png
Kazimierz_1.png
Kazimierz_2.png
...
```

**规则说明**：

| 项 | 要求 |
|---|---|
| 类名 | 必须与 `GameDataManager.Categories` 中的分类名**完全一致**（区分大小写） |
| 分隔符 | 使用下划线 `_` |
| 序号 | 从 `0` 开始，连续递增 |
| 格式 | 推荐 PNG（也支持 JPG） |

**AssetBundleManager** 会在加载 AB 包时，按 `_` 分隔文件名，前缀作为分类名分组缓存。例如 `Kazimierz_3.png` 会被归类到 `Kazimierz` 分类的第 4 张图。

### ⚠️ 版权提醒

本仓库**不包含**任何受版权保护的图片素材。

如需运行游戏，请自行准备图片放到 `Assets/PuzzleArt/分类名/` 目录下，并保证你拥有这些图片的使用权。**请勿将受版权保护的图片用于商业用途。**

---

## 🚀 快速开始

### 1. 克隆项目

```bash
git clone https://github.com/happyljy/PuzzleGame.git
```

### 2. 配置 Unity

1. 使用 **Unity 6000.2.1f1** 打开项目
2. 进入 `Edit → Preferences → External Tools`，设置：
   - **JDK**：指向 `jdk-17.0.20.8-hotspot` 目录
   - **Android NDK**：指向 `android-ndk-r27c` 目录
   - **Android SDK**：指向你的 SDK 目录

### 3. 导入 Native Gallery 1.9.4

1. 从 [UnityNativeGallery Releases](https://github.com/yasirkula/UnityNativeGallery/releases) 下载 **1.9.4** 版本的 `.unitypackage`
2. 在 Unity 中 `Assets → Import Package → Custom Package` 导入

### 4. 构建设置

进入 `Edit → Project Settings → Player → Android`：

| 设置项 | 值 |
|--------|-----|
| Minimum API Level | Android 6.0 (API 23) |
| Target API Level | Android 15.0 (API 35) |
| API Compatibility Level | .NET Standard 2.1 |
| Active Input Handling | **Both** |

### 5. 准备图片资源

按上文「图片资源规范」把图片放入 `Assets/PuzzleArt/分类名/` 目录，并按 `类名_序号.png` 命名。

### 6. 设置图片压缩（重要）

选中 `Assets/PuzzleArt/` 下的所有图片，在 Inspector 的 **Android** 标签页设置：

| 设置项 | 值 |
|--------|-----|
| Override for Android | ☑ 勾上 |
| Max Size | 1024 |
| Format | **ASTC** |
| Compressor Quality | Normal |
| Block Size | **6x6** |

> 这样可以大幅降低内存占用（相比未压缩的 RGBA32 省约 6.5 倍）。

### 7. 构建 AssetBundle

在 Unity 菜单栏选择 `Tools → Build AssetBundles`，AB 包会输出到 `Assets/StreamingAssets/AssetBundles`。

### 8. 打包 APK

`File → Build Settings → Android → Build`。

---

## 🎮 玩法说明

### 拼图操作

- **拖拽**：点击碎片列表中的碎片，在拼图区域按住碎片拖动到目标位置
- **旋转**：点击拼图区域中的碎片，顺时针旋转 90°
- **吸附**：当碎片位置和角度正确时，自动锁定
- **提示**：按住提示按钮查看原图
- **返回碎片**：点击返回按钮，未锁定的碎片回到列表
- **双指缩放**：在拼图区域双指捏合，可放大 / 缩小拼图内容（1×~2×），放大后可查看碎片细节
- **双指平移**：在拼图区域双指拖动，可平移拼图内容

### 进度保存

- 每次锁定碎片后自动保存进度
- 退出后再次进入同一张图、同一难度，已锁定的碎片会恢复
- 点击 **重新开始** 按钮清除进度，所有碎片回到列表

### 局域网分享

1. **分享方**：点击「分享」→ 启动 TCP 服务器 + UDP 广播
2. **接收方**：点击「接收」→ 搜索同一 Wi-Fi 下的设备
3. 连接成功后，分享方选择图片并发送就绪广播
4. 接收方点击「确认下载」下载全部图片到「共享」分类

### ⚠️ 已知限制

这是一个**功能演示 Demo**，以下几点属于已知局限：

- **高难度下碎片偏小**：10×10 难度下碎片只有约 70×70 像素，视觉上内容相近、辨识度低，容易拖错。
- **图片内容相似问题**：如果原图中存在大片相似色块或重复纹理，高难度碎片会难以区分，属正常现象，建议选择纹理丰富的图片。
- **局域网分享**：仅支持同一 Wi-Fi 下的设备，跨网段 / AP 隔离网络不可用。

---

## 🔧 技术要点

### 局域网通信协议

所有消息统一格式：`[1字节类型][4字节长度][payload]`

- `0x01` = 文本消息（UTF-8）
- `0x02` = 文件消息（二进制）

使用 **UDP 广播**进行设备发现，**TCP** 进行文件传输。

### 线程安全

- 后台线程（UDP/TCP 回调）通过 `EnqueueMainThread` 调度到主线程
- Unity API（如 `Application.persistentDataPath`）在 `Awake` 中缓存，避免后台线程调用

### 输入系统

项目使用 **Active Input Handling = Both** 模式，同时兼容：

- 旧版 `Input` 类（用于触摸判断、双指缩放）
- 新版 Input System（预留给后续扩展）

> 如果只选了 `Input System Package (New)`，`Input.touchCount` / `Input.GetTouch` 会失效，双指缩放和拖拽都会失灵。

### 图片加载

- **内置分类**：打包为 AssetBundle，通过 `AssetBundleManager` 加载
- **上传 / 共享图片**：通过 `ImageLoader.LoadSpriteFromFileAsync` 异步加载本地文件
- Android 特殊格式（HEIC/WebP）通过 `AndroidImageDecoder` 原生解码

### 拼图进度

- 用 `PlayerPrefs` 存储，key 格式：`PuzzleProgress_分类_图片索引_难度`
- 值是一个 "0/1" 位串，长度 = 碎片总数
- 每次锁定碎片后立即保存，拼图完成时清除

---

## 🙏 鸣谢

感谢以下创作者的开源素材：

### 🎵 音效与音乐

以下音频素材来自 [freesound.org](https://freesound.org/)：

- MadGravityStudio
- mokasza
- MATUSTRM
- KrystaPhillps
- CAT-FOX_ALEX
- Sadiquecat
- SilverIllusionist
- odarmonix

### 🎨 美术资源

- **Prinbles**（来自 [itch.io](https://itch.io/)）—— 按钮、部分面板图片

---

## 📄 许可证

本项目**代码**采用 [MIT License](LICENSE) 开源。

### 关于第三方素材

本项目包含的**第三方素材**（音频、美术）遵循各自的原授权协议。

**部分素材可能限制商业使用**，如需将本项目用于商业目的，请确保：
1. 替换掉所有受限素材，或
2. 取得原作者的商业授权

### 关于拼图图片

拼图关卡使用的图片为开发者线下拍摄，内容涉及第三方版权内容（如《明日方舟》角色立绘），版权归原版权方所有。

**本项目仅供个人学习与技术交流，不用于任何商业目的。** 如版权方有异议，请联系删除。

---

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

---

## 📧 联系

- 邮箱：lqq2002ljy@gmail.com
- GitHub：[@happyljy](https://github.com/happyljy)
