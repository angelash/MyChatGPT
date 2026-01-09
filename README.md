# AudioBridge（Windows ⇄ Android 双向语音桥接）

本仓库当前以**文档驱动开发**为主：先把协议/模块合同/验收口径钉死，再逐步落地 Windows（C#）与 Android（Kotlin）工程。

## 开工必读（按顺序）

- `docs/00-执行准备与决策点.md`：**你需要确认什么 & 需要你手动操作什么**
- `docs/01-仓库结构与工程初始化.md`：C# / Android 工程是否需要你创建、推荐结构
- `docs/02-推进计划（里程碑+工作包）.md`：里程碑、工作包依赖、验收标准
- `design.md`：总体设计（架构/协议/模块拆分/工作包原始定义）

## 当前状态

（2026-01-09）

- **已落地**
  - **协议**：ABP/1.0（二进制帧 + 控制消息 + 测试向量，Windows/Android 双端实现）
  - **Windows**：托盘程序 + WebSocket Server（`/abp`）+ LoopbackCapture + VirtualMicRenderer
  - **Android**：App（host/token + 上下行开关）+ WebSocket Client + AudioRecord/AudioTrack
  - **可靠性/可观测性**：心跳；Windows 状态窗 + 文件日志；Android 状态文本/Logcat
  - **省流优化（上下行）**：ADPCM（IMA）压缩 + 静音停发（DTX）+ bytes/丢弃帧统计
- **待完善**
  - 自动重连（Android/Windows）
  - 一键诊断（测试音/回路测试）
  - 设置 UI（设备/端口/codec 参数可视化）
  - 更完整的 jitter buffer / 缺包处理
  - 公网安全（WSS/TLS、鉴权增强）

## 怎么跑起来（当前已支持双向音频 + 省流）

### Windows（托盘 + 音频桥接 + WS Server）

1. 打开 `src/windows/AudioBridge.sln`
2. 运行 `AudioBridge.Agent.Tray`
3. 托盘右键 → **Start**（默认监听 **21347**）
4. （可选）托盘双击 → 打开状态窗，查看 `codec/bytes/丢弃帧` 统计

> 备注：需要先安装虚拟声卡（例如 VB-CABLE），并放行 Windows 防火墙入站端口（21347）。

### Android（AudioBridgeClient）

1. Android Studio 打开 `src/android/AudioBridgeClient`
2. 运行 App
3. Host 输入支持：
   - `192.168.1.23:21347`（局域网直连）
   - `your.domain.com`（如已做端口映射/反代到 WS）
   - `ws://your.domain.com/abp`（完整 URL）
4. 点击 **连接**；按需开关 **上行/下行**；状态区会显示协商 `codec` 与 bytes

> 备注：Android 工程如果提示缺少 `gradle-wrapper.jar`，见 `src/android/AudioBridgeClient/README.md` 的导入说明。
