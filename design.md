下面给你一套**两端“完整程序设计文档 + 协议规范 + 可拆分的开发计划”**，目标是：

* **只依赖浏览器访问**（不碰 Realtime API，不注入网页、不抓 DOM）
* 你个人使用，**追求“基本可用 + 可长期维护”**
* 重点解决：**Windows 系统声音捕获 → 串到 Android**；**Android 麦克风 → 回灌成 Windows 虚拟麦克风**
* 你已经接受“首次用远程桌面手动点开网页语音”，后续只做音频链路常驻。

我会按“AI 开发可控上下文”的方式写：**先合同/接口，再模块，再验收**。你把这些文档直接丢到仓库 `/docs` 里，就能分块喂给 AI 写代码。

> 合规提醒（讲清楚就完事）：这套桥接是通用的系统音频通道，不应拿来做违反服务条款或当地法律的事情；我不会提供任何“绕过限制/规避风控”的网络手段与步骤。

---

# 文档 0：项目总览（AudioBridge v1）

## 0.1 一句话定义

AudioBridge 是一套**Android ⇄ Windows 的双向语音通道**：

* **下行（Windows→Android）**：抓 Windows 默认播放设备的系统输出（浏览器播什么就是什么）→ 编码 → 传到手机播放
* **上行（Android→Windows）**：手机麦克风 → 编码 → 传到 Windows → 解码后**播放到虚拟声卡的 Playback 端** → 浏览器选择虚拟声卡对应的 Recording 端作为麦克风输入

## 0.2 核心约束

* 只做**音频**（你画面用远程桌面）
* 第一版默认：**戴耳机**（不做复杂 AEC；不戴耳机很容易自激）
* 第一版默认：局域网或你能直连 Windows（端口可达）；不做“穿透/中继平台”的复杂产品化（可以留扩展位）

## 0.3 最终音频拓扑（必须保证）

1. **浏览器麦克风**固定选：虚拟声卡的 Recording 端（虚拟麦克风）
2. Windows Agent 把手机上行音频写入：虚拟声卡的 Playback 端（等价“对虚拟线说话”）
3. Windows Agent 从默认播放设备 loopback 抓到浏览器播出的 TTS → 发到手机

---

# 文档 1：系统架构与组件划分

## 1.1 组件清单

### Windows：AudioBridge.Agent（常驻服务/托盘程序）

* `DeviceManager`：枚举与锁定音频设备（默认播放设备 + 指定虚拟声卡 Playback 端）
* `LoopbackCapture`：WASAPI Loopback 抓系统输出
* `VirtualMicRenderer`：WASAPI Render 写入虚拟声卡 Playback
* `AudioProcessing`：重采样、单声道、增益、限幅、抖动缓冲（简化版）
* `TransportServer`：与 Android 建立连接（建议先 WebSocket；可升级 UDP/WebRTC）
* `ControlChannel`：握手鉴权、心跳、PTT/mute、统计信息
* `Logging`：结构化日志、故障快照（设备列表/采样率/缓冲水位）

### Android：AudioBridge.Client

* `Session`：连接管理、重连、网络切换
* `MicCapture`：AudioRecord 采集
* `SpeakerPlayback`：AudioTrack 播放
* `Codec`：Opus 编解码（或先 PCM MVP）
* `UI`：连接页、PTT、静音、状态（延迟/丢包/是否耳机）
* `AudioRoute`：强制耳机/蓝牙优先（第一版至少做“检测未插耳机就提示”）

## 1.2 传输方案选型（按“个人可用 + 实现可控”推荐）

你要“基本可用”且 C# 熟，我建议**v1 用 WebSocket 单连接承载音频+控制**（实现简单、可调试、跨网络环境比 UDP 更容易跑通）。代价是：网络抖动时可能比 UDP/WebRTC 更“闷/卡”。

**v1（推荐落地）**

* `WSS/WS`：控制消息 JSON + 音频帧二进制（同一连接）

**v1.1（可选增强）**

* 音频改 `UDP`（降低延迟），控制仍走 `WS`（握手下发 UDP 端口与会话密钥）

**v2（再考虑）**

* WebRTC（最好用但工程量更大；你个人未必需要）

> 你要的“上下文质量可控 + AI 开发”，v1 的 WS 最友好：抓包、日志、回放都简单。

---

# 文档 2：协议规范（ABP/1.0）

## 2.1 基本连接

* Android 主动连接 Windows：`ws://<host>:<port>/abp`（内网）或 `wss://`（公网建议 TLS）
* 单连接复用：

  * 文本帧：JSON 控制
  * 二进制帧：音频数据包

## 2.2 鉴权模型（个人使用，简单但别裸奔）

* 预共享 `token`（你手动输入或扫码下发）
* 握手时发 `HMAC`（可选）或直接 token（v1 可以先直给，局域网可接受；公网建议至少 HMAC）

### 控制消息：Hello（Android→Windows）

```json
{
  "type": "hello",
  "proto": "ABP/1.0",
  "deviceId": "android-xxxx",
  "token": "PSK_TOKEN",
  "cap": {
    "codec": ["opus"],
    "sampleRate": [48000],
    "frameMs": [20],
    "uplink": true,
    "downlink": true
  }
}
```

### Welcome（Windows→Android）

```json
{
  "type": "welcome",
  "sessionId": "sess-uuid",
  "selected": {
    "codec": "opus",
    "sampleRate": 48000,
    "channels": 1,
    "frameMs": 20
  },
  "server": {
    "heartbeatMs": 5000
  }
}
```

### Error

```json
{ "type": "error", "code": "AUTH_FAIL", "message": "invalid token" }
```

## 2.3 心跳

* Android 每 `heartbeatMs` 发：

```json
{ "type": "ping", "t": 1730000000 }
```

* Windows 回：

```json
{ "type": "pong", "t": 1730000000 }
```

## 2.4 控制命令（DataChannel 等价）

* PTT 开关（Android→Windows）

```json
{ "type": "ptt", "enabled": true }
```

* 上行静音（Android→Windows）

```json
{ "type": "muteUplink", "enabled": true }
```

* 下行静音（Android→Windows）

```json
{ "type": "muteDownlink", "enabled": true }
```

## 2.5 音频二进制帧格式（统一帧头，便于 AI 实现）

**Binary Frame Layout（Little Endian）**

```
0-1   : uint16  magic = 0xAB01
2     : uint8   version = 1
3     : uint8   streamId (1=downlink PC->Phone, 2=uplink Phone->PC)
4-7   : uint32  seq
8-11  : uint32  timestampSamples (48kHz sample clock)
12-13 : uint16  payloadLen
14..  : payload (Opus bytes)
```

**帧粒度**

* `frameMs = 20ms`（推荐）
* 48kHz 下 20ms = 960 samples

## 2.6 抖动缓冲策略（v1 简化版）

接收端维护 `JitterBuffer`：

* 目标缓冲：`60ms`（3 帧）
* 最大缓冲：`200ms`（超了就丢老包追实时）
* 缺包：调用 Opus PLC（解码时传 null）或简单静音填充（MVP）

> 第一版别追求极致，追求“不断流 + 不爆音”。

---

# 文档 3：Windows 端设计（AudioBridge.Agent）

## 3.1 运行形态

* v1：控制台 + 可选托盘（推荐托盘，方便看状态）
* 开机自启（可选）
* 崩溃自恢复（Windows 任务计划/服务化，v1 可不做）

## 3.2 关键功能需求（FR）

1. 设备枚举：列出播放设备、录音设备、虚拟声卡 Playback 设备
2. 选择策略：

   * 系统输出抓取：默认播放设备（或指定设备）
   * 注入设备：指定“虚拟声卡 Playback 端”（通过名称/设备ID匹配）
3. 下行：Loopback → 编码 → 推送给 Android
4. 上行：Android → 解码 → 写入虚拟声卡 Playback
5. 控制：mute/PTT/重连/统计
6. 日志：断流、设备丢失、采样率变化、缓冲水位

## 3.3 非功能需求（NFR）

* 延迟：端到端语音感知尽量 < 300ms（v1 WS 可能略高，能接受）
* 稳定性：设备变化/默认设备切换要能自恢复（至少记录并提示）
* 安全：token 鉴权；公网建议 WSS

## 3.4 模块详细设计

### 3.4.1 DeviceManager

**职责**

* 使用 MMDeviceEnumerator 枚举设备（NAudio 支持）
* 保存选中的设备 ID 到 `Devices.json`
* 运行时校验设备存在，否则降级策略：重新匹配“包含关键字的设备名”

**配置文件：Devices.json**

```json
{
  "preferredLoopbackOutDeviceId": "default",
  "virtualCablePlaybackDeviceId": "....",
  "virtualCableNameHints": ["CABLE", "Virtual", "VB-Audio"]
}
```

### 3.4.2 LoopbackCapture

**输入**：系统默认播放设备（或指定）
**输出**：PCM 帧（建议内部统一 `48kHz/mono/int16`）

**实现要点**

* NAudio `WasapiLoopbackCapture` 通常输出 `float stereo 48k`
* 处理链：

  1. stereo → mono（L+R)/2
  2. float → int16
  3. 切帧 20ms（960 samples）
  4. 推给 Encoder

### 3.4.3 Encoder/Decoder

* 推荐：`Concentus.Opus`（纯 C#，AI 写起来省心）
* 编码参数：

  * `OpusApplication.OPUS_APPLICATION_VOIP`
  * 帧长：20ms
  * 码率：先 24kbps～32kbps（语音够用）

### 3.4.4 TransportServer（WebSocket）

**职责**

* 监听端口
* 处理握手/鉴权
* 维护 session（单用户单连接即可）
* 发送下行二进制帧
* 接收上行二进制帧并转给 decoder

### 3.4.5 VirtualMicRenderer

**职责**

* 初始化指定虚拟声卡 Playback 设备
* 提供 `WritePcmFrame(short[] pcm48kMono)`，写入缓冲
* 缓冲策略：

  * 目标缓存 60-120ms（避免网络抖动导致断断续续）
  * 缓冲过大则丢旧帧（追实时）

**关键点**

* 使用 `BufferedWaveProvider`
* `DiscardOnBufferOverflow = true`（宁可丢也别卡死）

### 3.4.6 统计与诊断（强烈建议做，AI 开发也需要“可观测性”）

* `uplinkLossRate`（按 seq 统计）
* `jitterBufferMs`（当前缓冲时长）
* `audioUnderrunCount`（播放端缺料次数）
* `deviceReconnectCount`

---

# 文档 4：Android 端设计（AudioBridge.Client）

## 4.1 功能需求（FR）

1. 配置连接：host/port/token（或扫码导入）
2. 连接状态：连接中/已连接/断线重连
3. 上行采集：麦克风 PCM → 编码 → 发给 Windows
4. 下行播放：收到 Windows 音频 → 解码 → AudioTrack 播放
5. PTT / 静音：可控上行发送
6. 耳机检测：未插耳机弹出强提示（v1）

## 4.2 音频实现要点

### 4.2.1 MicCapture（AudioRecord）

* 采样率：48k
* 通道：mono
* 格式：PCM 16bit
* 读出后按 20ms 切帧（960 samples）→ Opus encode → send

### 4.2.2 SpeakerPlayback（AudioTrack）

* 同 48k mono PCM16
* 接收端 jitter buffer（与 Windows 同策略）
* 缺包用 PLC 或静音

### 4.2.3 AudioRoute

* 检测：`AudioManager.isWiredHeadsetOn`（或更现代的设备回调）
* v1 策略：

  * 未检测到耳机：UI 强提示 + 默认禁用上行（避免自激）
  * 允许用户强行继续（个人使用你自己决定）

## 4.3 UI（极简但够用）

* 顶部：连接状态 + 延迟/丢包
* 中间：Host/Token 配置
* 底部：

  * `Connect/Disconnect`
  * `PTT`（按住发声）
  * `Mute`（上行静音）

---

# 文档 5：开发计划（按合适粒度拆分，适配 AI 开发）

下面每个 Work Package（WP）都带**输入/输出/验收标准**，你可以一包一包喂给 AI 写，避免上下文污染。

## WP0：仓库与“合同先行”

**目标**：让 AI 不会自由发挥

* 建 `/docs`：放本文档
* 建 `/proto`：放 ABP/1.0 二进制帧定义 + JSON schema
* 建 `/tests`：放协议测试向量（固定输入输出）

**验收**

* 有一个 `ProtocolTestVectors.json`，包含 10 组（header+payloadLen+seq 递增）

---

## WP1：协议层实现（Windows + Android）

**输入**：ABP/1.0 定义
**输出**

* `BinaryFrame` 编解码（严格按字节布局）
* `SeqTracker`（丢包统计）
* 控制消息处理器（hello/welcome/ping）

**验收**

* 两端对同一 test vector 编码结果完全一致（字节级）

---

## WP2：Windows 音频 I/O MVP（不联网）

**目标**：先证明“能抓、能放”

1. `LoopbackCapture`：抓系统输出 → 写 wav 文件（10 秒）
2. `VirtualMicRenderer`：从 wav 播放到指定设备

**验收**

* wav 文件可播放且内容确实为系统声
* 播放到虚拟声卡后，浏览器/录音软件能从虚拟麦克风录到声音

---

## WP3：Android 音频 I/O MVP（不联网）

1. `MicCapture`：录 10 秒保存 PCM/WAV
2. `SpeakerPlayback`：播放本地 wav/pcm

**验收**

* 录音清晰
* 播放无爆音、无明显卡顿

---

## WP4：传输层 v1（WebSocket 单连接）

**输出**

* Windows：`TransportServer`（WS）
* Android：`TransportClient`（WS）
* 支持：hello/welcome + 二进制帧收发

**验收**

* Android 连接 Windows 成功
* 双向能稳定发 5 分钟心跳不中断

---

## WP5：端到端下行（Windows 系统声 → Android）

**输出**

* Windows：Loopback → Opus encode → send streamId=1
* Android：recv → Opus decode → 播放

**验收**

* 在 Android 耳机中能听到 Windows 正在播放的浏览器声音
* 延迟与卡顿“可接受”（个人使用标准：能跟上语音内容、不频繁断裂）

---

## WP6：端到端上行（Android 麦克风 → Windows 虚拟麦克风）

**输出**

* Android：mic → encode → send streamId=2（支持 PTT）
* Windows：recv → decode → VirtualMicRenderer

**验收**

* Windows 录音设备电平有变化
* 浏览器选择虚拟麦克风后，网页语音能识别到你讲话（至少能触发“正在聆听/有声输入”）

---

## WP7：稳定性与可观测性（决定你“能不能长期用”）

**输出**

* 重连策略（断线自动重连、回退指数）
* 设备丢失自动重绑（虚拟声卡重装/设备名变化）
* 统计面板（延迟估计、丢包、buffer 水位）

**验收**

* 人为断网 10 秒 → 自动恢复
* 切换默认播放设备 → 能提示或自动切回

---

## WP8：体验增强（可选，但建议做）

* 耳机强提示 + 一键测试回路（播测试音/录测试音）
* 上行 AGC（简单版：按 RMS 把电平拉到目标区间）
* 下行限幅（防爆音）

---

# 文档 6：AI 开发“上下文质量控制”方法（你要的重点）

你用 AI 开发时，最容易翻车的不是技术，是**上下文漂移**。按下面做，能把模型锁在轨道上：

## 6.1 永远“合同优先”

每次让 AI 写代码，只给它：

1. 本 WP 的目标与验收标准
2. 相关的接口定义（C# interface / Kotlin interface）
3. 现有目录结构
4. 不要把全项目都贴给它

## 6.2 每个模块都写“模块契约卡”（建议放 /docs/contracts）

模板（示例：VirtualMicRenderer）

* 职责：把 PCM48k/mono/int16 写入指定输出设备
* 输入：`short[] frame` 长度=960
* 行为：不足则补静音；过载则丢旧帧
* 线程：`WriteFrame` 可被网络线程调用，内部必须线程安全
* 日志：设备初始化失败、underrun 次数
* 测试：注入 1kHz 测试音，浏览器可录到



好，Windows 端定为**托盘常驻程序**是最适合你这个“个人长期用、只做音频桥接”的形态。下面我把前面文档里 Windows 端的“运行形态 / UI / 生命周期 / 与远程桌面共存”补齐到可直接开工的设计粒度，并同步更新开发计划里需要新增/调整的工作包。

---

# Windows 端改为托盘程序后的设计补充（AudioBridge.Agent.Tray）

## 1）形态选择与项目拆分

### 推荐 UI 技术栈

* **.NET 8 + WinForms 托盘宿主（NotifyIcon）**

  * 原因：托盘、右键菜单、单实例、后台常驻、与音频线程协作都最顺
  * 你十几年 C# 经验，上手成本最低、坑最少

### 解决“UI 不影响核心”的工程拆分

建议拆成 3 个项目（很利于 AI 分块写，避免上下文污染）：

1. `AudioBridge.Core`（纯逻辑库，无 UI）

   * `LoopbackCapture / VirtualMicRenderer / Codec / JitterBuffer / Session`
2. `AudioBridge.Transport`（网络层，无 UI）

   * `WebSocketServer / Protocol(ABP) / Auth / Metrics`
3. `AudioBridge.Agent.Tray`（WinForms 托盘程序）

   * 只做：托盘图标、菜单、设置窗、状态窗、日志入口、把 Core/Transport 启停起来

> 核心原则：**托盘只是“遥控器 + 状态面板”，音频/网络都在后台服务对象里跑**。

---

## 2）托盘交互规格（你每天用到的就这些）

### 2.1 托盘图标状态

用不同图标/tooltip 表达状态（对个人使用非常关键）：

* `灰色`：未启动（Stopped）
* `蓝色`：已启动、等待连接（Listening）
* `绿色`：已连接并在传输（Connected）
* `黄色`：有抖动/丢包明显（Degraded）
* `红色`：错误（设备丢失/鉴权失败/渲染失败）

Tooltip 示例：

* `AudioBridge: Connected | RTT ~ 80ms | Loss 0.3% | JB 60ms`

### 2.2 右键菜单（最小但够用）

* **Start / Stop**
* **Show Status**（打开状态窗）
* **Settings…**（设备/网络/编码/耳机策略）
* **Show Pairing**（显示 token + 二维码/字符串，方便手机录入）
* **Diagnostics**

  * `Play Test Tone`（下行测试：确认手机能听）
  * `Record Test (Virtual Mic)`（上行测试：确认浏览器能听）
  * `Open Logs Folder`
* **Exit**

> 个人使用不需要花哨 UI，但一定要有“测试音/打开日志”这俩救命按钮。

### 2.3 状态窗（建议做成可常开的小窗）

显示**实时指标**（文字即可，别一开始做图表）：

* 连接：客户端设备 ID、连接时长、心跳时间
* 下行：编码码率、发送帧率、丢包率、jitter buffer 水位
* 上行：接收帧率、丢包率、虚拟麦克风渲染缓冲水位、underrun 次数
* 设备：当前 loopback 绑定的输出设备名、virtual cable playback 设备名
* 最近错误：最后 10 条 error/warn

---

## 3）关键：与远程桌面共存的“设备锁定策略”（不做这个你会反复掉坑）

你明确“画面用远程桌面”，那就必须正视一个现实：

> **RDP 会经常改你的默认音频设备**（出现 “Remote Audio” / “Remote Audio Microphone”），导致 loopback 抓错声、或浏览器麦克风被换掉。

### 3.1 规则：永远不用“默认设备”做关键绑定

在设置里必须让用户（你自己）选择并保存：

* `LoopbackOutputDeviceId`：明确绑定到**本机真实播放设备**（扬声器/耳机）
* `VirtualCablePlaybackDeviceId`：明确绑定到虚拟线的 playback 端

并在启动时校验：

* 如果发现当前默认输出设备是 `Remote Audio`，但你绑定的是本机设备，则：

  * 仍按绑定设备抓取（不理默认）
  * 在状态里提示：`Default device changed by RDP; using pinned device`

### 3.2 浏览器侧固定虚拟麦克风

浏览器里选择麦克风时，只选一次：

* “Virtual Cable Recording”（虚拟麦克风）
  之后你就不要让浏览器跟着系统默认跑。

### 3.3 诊断按钮要能一眼定位“是不是 RDP 搞的”

Diagnostics 增加一个检查：

* `Check Remote Audio Interference`
  输出：
* 当前默认输出/输入设备
* 是否存在 Remote Audio 设备
* 当前 pinned device 是否可用

---

## 4）托盘程序生命周期与线程模型（避免 UI 卡死）

### 4.1 单实例

托盘程序必须单实例（否则两个实例抢音频设备必死）。

* 用 `Mutex("AudioBridge.Agent.Tray")`

### 4.2 UI 与后台线程隔离

* UI 线程只接收事件更新状态
* Core/Transport 在后台线程运行
* 通过 `SynchronizationContext.Post` 把状态回传 UI

### 4.3 后台服务的“硬停止”顺序（要写进契约）

Stop 时严格顺序，避免音频设备被占用：

1. 停止接收新连接（Transport）
2. 停止 loopback capture
3. 停止 virtual render（waveOut stop）
4. 释放 codec 与缓冲
5. 更新 UI 状态为 Stopped

---

## 5）设置与配置（个人使用也要做“可恢复”）

### 5.1 配置存储位置

* `%AppData%/AudioBridge/devices.json`
* `%AppData%/AudioBridge/settings.json`
* `%AppData%/AudioBridge/token.txt`（或 Windows Credential Manager，v1 可先明文 + 本机使用）

### 5.2 Settings 界面建议分 3 个 Tab

**Tab A：Connection**

* Listen Port
* Token（显示/重置/复制）
* Auto-start server（开机后自动 Start）

**Tab B：Audio Devices**

* Loopback output device（下拉）
* Virtual cable playback device（下拉）
* Test buttons（下行测试/上行测试）

**Tab C：Codec**

* frameMs：20（固定，v1 不开放）
* bitrate：24/32kbps
* jitter buffer target：60ms（可调）

---

# 开发计划更新（新增/调整工作包）

你之前的 WP0~WP8 仍然成立，但要加一个“托盘 UI 与可用性”工作包，并对几个 WP 的验收点补充 RDP 场景。

## 新增 WP-UI1：托盘宿主与状态联动（高优先级）

**输出**

* WinForms `NotifyIcon + ContextMenuStrip`
* `TrayAppContext`（继承 `ApplicationContext`）
* `StatusWindow` + `SettingsWindow`
* 图标状态机（Stopped/Listening/Connected/Degraded/Error）

**验收**

* 关闭窗口不退出（仍驻留托盘）
* Start/Stop 可反复点击 20 次不崩、不占设备
* 状态窗实时刷新指标（每秒 1 次即可）

## 调整 WP2 / WP5 / WP6：加入“RDP 干扰”验收

* 当默认设备被切到 Remote Audio 时：

  * 仍能按 pinned 设备抓系统声（WP5）
  * 虚拟麦克风渲染不受默认输入变化影响（WP6）
  * UI 有明确警告提示（WP-UI1）

## 新增 WP-DIAG1：一键诊断与测试音（强烈建议）

**输出**

* `Play Test Tone`：Windows 端生成 1kHz 20ms 帧 → 直接走下行链路 → Android 听见
* `Uplink Loop Test`：Android 发上行 → Windows 注入虚拟麦克风 → Windows 本地录音/电平显示可见

**验收**

* 两个按钮可在不打开浏览器的情况下证明链路通

---

# 你接下来“喂 AI 写代码”的推荐顺序（保证上下文干净）

1. **WP1 协议层（两端）**：先把 ABP/1.0 帧头/序号/统计写死
2. **WP2 Windows 音频 I/O（无网络）**：loopback + virtual render 都跑通
3. **WP-UI1 托盘宿主**：把 Start/Stop 绑定到后台服务骨架
4. **WP4 WS 传输层**：先跑通 hello/welcome + ping/pong
5. **WP5 下行**：你能在手机耳机听到 Windows 系统声音
6. **WP6 上行**：你说话网页能听见（虚拟麦克风）
7. **WP-DIAG1**：把故障定位能力补上（后面省无数时间）

---

# 附录：开工执行入口（已落盘到本仓库）

你现在准备进入“开始写工程”的阶段，建议先看这三份执行文档（它们把**待决策点**和**需要你手工操作的外部依赖**写成了清单）：

* `docs/00-执行准备与决策点.md`
* `docs/01-仓库结构与工程初始化.md`
* `docs/02-推进计划（里程碑+工作包）.md`

> 你确认完 `docs/00` 里的选择后，就可以直接进入 WP0/WP1 开始落工程骨架与协议测试向量。
