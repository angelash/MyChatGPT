using System.Drawing;
using AudioBridge.Core.Audio;
using AudioBridge.Transport.Server;

namespace AudioBridge.Agent.Tray;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startMenuItem;
    private readonly ToolStripMenuItem _stopMenuItem;
    private TrayState _state = TrayState.Stopped;
    private AbpWebSocketServer? _server;
    private AudioBridgeService? _audioService;

    public TrayAppContext()
    {
        _startMenuItem = new ToolStripMenuItem("Start", null, (_, _) => Start());
        _stopMenuItem = new ToolStripMenuItem("Stop", null, (_, _) => Stop()) { Enabled = false };

        var showStatusItem = new ToolStripMenuItem("Show Status", null, (_, _) => ShowStatus());
        var showDevicesItem = new ToolStripMenuItem("Show Devices", null, (_, _) => ShowDevices());
        var openDocsItem = new ToolStripMenuItem("Open Docs", null, (_, _) => OpenDocs());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showStatusItem);
        menu.Items.Add(showDevicesItem);
        menu.Items.Add(openDocsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = BuildTooltip(),
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowStatus();

        UpdateUiForState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _server?.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore
            }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Start()
    {
        _ = StartAsync();
    }

    private void Stop()
    {
        _ = StopAsync();
    }

    private void ShowStatus()
    {
        // MVP：先弹一个简易对话框；后续替换为独立 StatusWindow（WP-UI1）
        var audioStatus = _audioService?.GetStatus();
        var statusText = $"状态：{_state}\n";

        // WebSocket 服务器状态
        if (_server != null)
        {
            statusText += $"\nWebSocket 服务器：{(_server.IsRunning ? "运行中" : "已停止")}\n";
            statusText += $"  - 端口：{_server.Port}\n";
            statusText += $"  - Android 连接：{(_server.HasActiveSession ? "✓ 已连接" : "✗ 未连接")}\n";
            statusText += $"  - 下行帧（发→手机）：{_server.DownlinkFramesSent}\n";
            statusText += $"  - 上行帧（收←手机）：{_server.UplinkFramesReceived}\n";
        }

        if (audioStatus != null)
        {
            statusText += $"\n音频桥接：{(audioStatus.IsRunning ? "运行中" : "已停止")}\n";
            statusText += $"  - Loopback 捕获：{(audioStatus.IsLoopbackCapturing ? "✓" : "✗")}\n";
            statusText += $"  - 虚拟麦克风：{(audioStatus.IsVirtualMicRendering ? "✓" : "✗")}\n";
            statusText += $"  - 缓冲：{audioStatus.VirtualMicBufferedMs}ms\n";
            statusText += $"  - 欠载次数：{audioStatus.VirtualMicUnderrunCount}\n";
            statusText += $"  - 已写入帧（上行→虚拟麦）：{audioStatus.VirtualMicFramesWritten}\n";
        }

        statusText += "\n提示：v1 默认建议戴耳机使用（避免自激）。";

        MessageBox.Show(statusText, "AudioBridge Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowDevices()
    {
        using var dm = new Core.Devices.DeviceManager();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 播放设备（Render）===");
        foreach (var dev in dm.GetRenderDevices())
        {
            sb.AppendLine($"  [{(dev.IsDefault ? "默认" : "    ")}] {dev.FriendlyName}");
        }

        sb.AppendLine();
        sb.AppendLine("=== 录制设备（Capture）===");
        foreach (var dev in dm.GetCaptureDevices())
        {
            sb.AppendLine($"  [{(dev.IsDefault ? "默认" : "    ")}] {dev.FriendlyName}");
        }

        var cablePlayback = dm.FindVirtualCablePlayback();
        var cableRecording = dm.FindVirtualCableRecording();

        sb.AppendLine();
        sb.AppendLine("=== VB-CABLE 状态 ===");
        sb.AppendLine($"  CABLE Input (播放端)：{(cablePlayback != null ? "✓ 已找到" : "✗ 未找到")}");
        sb.AppendLine($"  CABLE Output (录制端)：{(cableRecording != null ? "✓ 已找到" : "✗ 未找到")}");

        MessageBox.Show(sb.ToString(), "AudioBridge Devices", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenDocs()
    {
        try
        {
            var docsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
            if (!Directory.Exists(docsPath))
            {
                MessageBox.Show("未找到 docs 目录。", "AudioBridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = docsPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开 docs 失败：{ex.Message}", "AudioBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private async Task StartAsync()
    {
        try
        {
            // 1. 启动音频桥接服务
            _audioService ??= new AudioBridgeService();
            _audioService.Error += OnAudioError;
            _audioService.Start();

            if (!_audioService.IsRunning)
            {
                _state = TrayState.Error;
                UpdateUiForState();
                return;
            }

            // 2. 启动 WebSocket 服务器
            // TODO: 从 settings.json 读取端口/token；目前先硬编码默认端口
            _server ??= new AbpWebSocketServer(port: 21347, token: null);

            // 连接音频流：下行帧（系统声）-> 发送给 Android
            _audioService.DownlinkFrameAvailable += OnDownlinkFrame;

            // 连接音频流：上行帧（Android 麦克风）-> 写入虚拟麦克风
            _server.UplinkFrameReceived += OnUplinkFrame;

            await _server.StartAsync();

            _state = TrayState.Listening;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(1500, "AudioBridge", $"已启动：监听 {_server.Port}，音频桥接已就绪", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _state = TrayState.Error;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(2000, "AudioBridge", $"启动失败：{ex.Message}", ToolTipIcon.Error);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            // 1. 停止 WebSocket 服务器
            if (_server is not null)
            {
                _server.UplinkFrameReceived -= OnUplinkFrame;
                await _server.StopAsync();
            }

            // 2. 停止音频桥接服务
            if (_audioService is not null)
            {
                _audioService.DownlinkFrameAvailable -= OnDownlinkFrame;
                _audioService.Error -= OnAudioError;
                _audioService.Stop();
            }

            _state = TrayState.Stopped;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(1500, "AudioBridge", "已停止。", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _state = TrayState.Error;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(2000, "AudioBridge", $"停止失败：{ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OnDownlinkFrame(byte[] pcmFrame)
    {
        // 下行音频（系统声）-> 发送给连接的 Android 客户端
        _server?.SendDownlinkFrame(pcmFrame);
    }

    private void OnUplinkFrame(byte[] pcmFrame)
    {
        // 上行音频（Android 麦克风）-> 写入虚拟麦克风
        _audioService?.WriteUplinkFrame(pcmFrame);
    }

    private void OnAudioError(string source, Exception? ex)
    {
        _notifyIcon.ShowBalloonTip(2000, "AudioBridge", $"音频错误 [{source}]: {ex?.Message ?? "未知错误"}", ToolTipIcon.Warning);
    }

    private void UpdateUiForState()
    {
        _startMenuItem.Enabled = _state == TrayState.Stopped;
        _stopMenuItem.Enabled = _state != TrayState.Stopped;
        _notifyIcon.Text = BuildTooltip();
    }

    private string BuildTooltip()
    {
        // Tooltip 不能太长（Windows 限制），先放最小信息
        return _state switch
        {
            TrayState.Stopped => "AudioBridge: Stopped",
            TrayState.Listening => "AudioBridge: Listening",
            TrayState.Connected => "AudioBridge: Connected",
            TrayState.Degraded => "AudioBridge: Degraded",
            TrayState.Error => "AudioBridge: Error",
            _ => "AudioBridge",
        };
    }

    private enum TrayState
    {
        Stopped,
        Listening,
        Connected,
        Degraded,
        Error,
    }
}

