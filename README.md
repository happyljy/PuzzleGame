# 🧩 PuzzleGame

一个基于 Unity 的图片拼图游戏，支持多分类图片、多种难度、图片上传、局域网图片分享与拼图进度保存。

---

## 📖 项目简介

PuzzleGame 是一款休闲拼图游戏，玩家从图库中选择图片，将其切割为 N×N 的碎片，通过拖拽和旋转完成拼图。项目支持：

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
| 最低 API 等级 | Android 6.0 Marshmallow (API 23) |
| API 兼容性等级 | .NET Standard 2.1 |

> **注意**：Unity 6000.0+ 版本需要 JDK 17 和 NDK r27c[reference:0][reference:1]。

---

## 📁 项目结构
Assets/
├── Scripts/
│ ├── MainMenuManager.cs # 主菜单管理器
│ ├── GameManager.cs # 拼图游戏主逻辑
│ ├── PuzzlePiece.cs # 拼图碎片交互
│ ├── GameDataManager.cs # 全局数据持久化
│ ├── LANShareManager.cs # 局域网分享（TCP/UDP）
│ ├── AssetBundleManager.cs # AssetBundle 加载与分类
│ ├── ImageLoader.cs # 图片异步加载
│ ├── AndroidImageDecoder.cs # Android 原生解码
│ ├── SoundManager.cs # 全局音频管理
│ ├── UnityMainThreadDispatcher.cs# 主线程调度器
│ ├── LoadingDots.cs # 加载动画
│ ├── SafeArea.cs # 刘海屏适配
│ ├── SplashAnimation.cs # 开场动画
│ ├── ButtonClickSound.cs # 按钮音效
│ └── TextBreathing.cs # 文字呼吸效果
├── Editor/
│ └── BuildAssetBundles.cs # AssetBundle 构建工具
├── Resources/
│ └── Art/
│ └── HeadPicture/ # 头像资源
├── StreamingAssets/
│ └── AssetBundles/ # 构建后的 AB 包
├── Scenes/
│ ├── SplashScene.unity # 开场动画场景
│ ├── LevelScene.unity # 主菜单场景
│ └── GameScene.unity # 拼图游戏场景
└── ...


🎮 玩法说明
拼图操作
拖拽：点击碎片列表中的碎片，在拼图区域按住碎片拖动到目标位置

旋转：点击拼图区域中的碎片，顺时针旋转 90°

吸附：当碎片位置和角度正确时，自动锁定

提示：按住提示按钮查看原图

缩放：双指捏合缩放拼图区域

返回碎片：点击返回按钮，未锁定的碎片回到列表

进度保存
每次锁定碎片后自动保存进度

退出后再次进入同一张图、同一难度，已锁定的碎片会恢复

点击 重新开始 按钮清除进度，所有碎片回到列表

局域网分享
分享方：点击「分享」→ 启动 TCP 服务器 + UDP 广播

接收方：点击「接收」→ 搜索同一 Wi-Fi 下的设备

连接成功后，分享方选择图片并发送就绪广播

接收方点击「确认下载」下载全部图片到「共享」分类

🔧 技术要点
局域网通信协议
所有消息统一格式：[1字节类型][4字节长度][payload]

0x01 = 文本消息（UTF-8）

0x02 = 文件消息（二进制）

使用 UDP 广播进行设备发现，TCP 进行文件传输。

线程安全
后台线程（UDP/TCP 回调）通过 EnqueueMainThread 调度到主线程

Unity API（如 Application.persistentDataPath）在 Awake 中缓存，避免后台线程调用

图片加载
使用 ImageLoader.LoadSpriteFromFileAsync 进行异步加载

Android 特殊格式（HEIC/WebP）通过 AndroidImageDecoder 原生解码
