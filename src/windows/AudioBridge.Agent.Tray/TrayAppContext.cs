using System.Drawing;
using AudioBridge.Core.Audio;
using AudioBridge.Core.Logging;
using AudioBridge.Transport.Server;

namespace AudioBridge.Agent.Tray;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startMenuItem;
    private readonly ToolStripMenuItem _stopMenuItem;
    private readonly FileLogger _logger;
    private readonly StatusForm _statusForm;
    private TrayState _state = TrayState.Stopped;
    private AbpWebSocketServer? _server;
    private AudioBridgeService? _audioService;

    public TrayAppContext()
    {
        _logger = FileLogger.Instance;
        _logger.Info("TrayApp", "AudioBridge 托盘应用启动");

        _startMenuItem = new ToolStripMenuItem("Start", null, (_, _) => Start());
        _stopMenuItem = new ToolStripMenuItem("Stop", null, (_, _) => Stop()) { Enabled = false };

        var showStatusItem = new ToolStripMenuItem("Show Status", null, (_, _) => ShowStatus());
        var showDevicesItem = new ToolStripMenuItem("Show Devices", null, (_, _) => ShowDevices());
        var openLogItem = new ToolStripMenuItem("Open Log File", null, (_, _) => OpenLogFile());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showStatusItem);
        menu.Items.Add(showDevicesItem);
        menu.Items.Add(openLogItem);
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

        // 创建状态窗口
        _statusForm = new StatusForm();
        _statusForm.SetDataSources(
            () => _server,
            () => _audioService,
            () => _state.ToString()
        );

        UpdateUiForState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger.Info("TrayApp", "正在关闭...");
            try
            {
                _server?.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore
            }

            _statusForm.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _logger.Dispose();
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
        _statusForm.Show();
        _statusForm.BringToFront();
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

    private void OpenLogFile()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _logger.GetLogFilePath(),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开日志文件失败：{ex.Message}", "AudioBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private async Task StartAsync()
    {
        _logger.Info("TrayApp", "正在启动服务...");
        try
        {
            // 1. 启动音频桥接服务
            _audioService ??= new AudioBridgeService();
            _audioService.Error += OnAudioError;
            _audioService.Start();
            _logger.Info("Audio", $"音频服务已启动，IsRunning={_audioService.IsRunning}");

            if (!_audioService.IsRunning)
            {
                _logger.Error("Audio", "音频服务启动失败");
                _state = TrayState.Error;
                UpdateUiForState();
                return;
            }

            // 2. 启动 WebSocket 服务器
            _server ??= new AbpWebSocketServer(port: 21347, token: null);
            _server.OnLog = (level, msg) => _logger.Log(level, "WebSocket", msg);

            // 连接音频流：下行帧（系统声）-> 发送给 Android
            _audioService.DownlinkFrameAvailable += OnDownlinkFrame;

            // 连接音频流：上行帧（Android 麦克风）-> 写入虚拟麦克风
            _server.UplinkFrameReceived += OnUplinkFrame;
            _server.SessionConnected += OnSessionConnected;
            _server.SessionDisconnected += OnSessionDisconnected;

            await _server.StartAsync();
            _logger.Info("WebSocket", $"WebSocket 服务器已启动，端口={_server.Port}");

            _state = TrayState.Listening;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(1500, "AudioBridge", $"已启动：监听 {_server.Port}，音频桥接已就绪", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.Error("TrayApp", $"启动失败：{ex}");
            _state = TrayState.Error;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(2000, "AudioBridge", $"启动失败：{ex.Message}", ToolTipIcon.Error);
        }
    }

    private async Task StopAsync()
    {
        _logger.Info("TrayApp", "正在停止服务...");
        try
        {
            // 1. 停止 WebSocket 服务器
            if (_server is not null)
            {
                _server.UplinkFrameReceived -= OnUplinkFrame;
                _server.SessionConnected -= OnSessionConnected;
                _server.SessionDisconnected -= OnSessionDisconnected;
                await _server.StopAsync();
                _logger.Info("WebSocket", "WebSocket 服务器已停止");
            }

            // 2. 停止音频桥接服务
            if (_audioService is not null)
            {
                _audioService.DownlinkFrameAvailable -= OnDownlinkFrame;
                _audioService.Error -= OnAudioError;
                _audioService.Stop();
                _logger.Info("Audio", "音频服务已停止");
            }

            _state = TrayState.Stopped;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(1500, "AudioBridge", "已停止。", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.Error("TrayApp", $"停止失败：{ex}");
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

    private void OnSessionConnected(string deviceId)
    {
        _logger.Info("WebSocket", $"客户端已连接：{deviceId}");
        _state = TrayState.Connected;
        UpdateUiForState();
        _notifyIcon.ShowBalloonTip(1500, "AudioBridge", $"客户端已连接：{deviceId}", ToolTipIcon.Info);
    }

    private void OnSessionDisconnected(string deviceId, string reason)
    {
        _logger.Info("WebSocket", $"客户端已断开：{deviceId}，原因：{reason}");
        _state = TrayState.Listening;
        UpdateUiForState();
        _notifyIcon.ShowBalloonTip(1500, "AudioBridge", $"客户端已断开：{reason}", ToolTipIcon.Warning);
    }

    private void OnAudioError(string source, Exception? ex)
    {
        _logger.Error("Audio", $"[{source}] {ex?.Message ?? "未知错误"}");
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
