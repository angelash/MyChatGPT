# AudioBridge（Windows ⇄ Android 双向语音桥接）

本仓库当前以**文档驱动开发**为主：先把协议/模块合同/验收口径钉死，再逐步落地 Windows（C#）与 Android（Kotlin）工程。

## 开工必读（按顺序）

- `docs/00-执行准备与决策点.md`：**你需要确认什么 & 需要你手动操作什么**
- `docs/01-仓库结构与工程初始化.md`：C# / Android 工程是否需要你创建、推荐结构
- `docs/02-推进计划（里程碑+工作包）.md`：里程碑、工作包依赖、验收标准
- `design.md`：总体设计（架构/协议/模块拆分/工作包原始定义）

## 当前状态

- 已落盘：执行准备与决策点、工程初始化建议、推进计划、`proto/` 与 `tests/`、协议测试向量
- 已创建工程骨架：
  - Windows：`src/windows/AudioBridge.sln`（托盘宿主 + 协议层 + WS Server 骨架）
  - Android：`src/android/AudioBridgeClient`（minSdk=21，UI/协议层/WS Client 骨架）

## 怎么跑起来（当前是 WP4 骨架：只到握手/心跳，不含音频）

### Windows（托盘 + WS Server）

1. 打开 `src/windows/AudioBridge.sln`
2. 运行 `AudioBridge.Agent.Tray`
3. 托盘右键 → **Start**（默认监听 **21347**）

### Android（WS Client）

1. Android Studio 打开 `src/android/AudioBridgeClient`
2. 运行 App
3. 填写 Host（Windows 的局域网 IP）+ Port（默认 21347）→ **Connect**

> 备注：Android 工程如果提示缺少 `gradle-wrapper.jar`，见 `src/android/AudioBridgeClient/README.md` 的导入说明。
