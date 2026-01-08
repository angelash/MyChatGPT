using System.Drawing;
using AudioBridge.Core.Audio;
using AudioBridge.Core.Logging;
using AudioBridge.Transport.Server;

namespace AudioBridge.Agent.Tray;

/// <summary>
/// 实时状态窗口
/// </summary>
public class StatusForm : Form
{
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly TextBox _logTextBox;
    private readonly Label _statusLabel;
    private readonly Label _wsStatusLabel;
    private readonly Label _audioStatusLabel;
    private readonly Label _statsLabel;

    private Func<AbpWebSocketServer?>? _getServer;
    private Func<AudioBridgeService?>? _getAudioService;
    private Func<string>? _getState;

    public StatusForm()
    {
        Text = "AudioBridge 状态监控";
        Size = new Size(700, 550);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(500, 400);

        // 状态面板
        var statusPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 140,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(30, 30, 30),
        };

        _statusLabel = new Label
        {
            Text = "状态：未知",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Location = new Point(10, 10),
            AutoSize = true,
        };

        _wsStatusLabel = new Label
        {
            Text = "WebSocket：-",
            ForeColor = Color.LightGray,
            Font = new Font("Consolas", 10),
            Location = new Point(10, 40),
            AutoSize = true,
        };

        _audioStatusLabel = new Label
        {
            Text = "音频：-",
            ForeColor = Color.LightGray,
            Font = new Font("Consolas", 10),
            Location = new Point(10, 65),
            AutoSize = true,
        };

        _statsLabel = new Label
        {
            Text = "统计：-",
            ForeColor = Color.LightGray,
            Font = new Font("Consolas", 10),
            Location = new Point(10, 90),
            AutoSize = true,
        };

        statusPanel.Controls.Add(_statusLabel);
        statusPanel.Controls.Add(_wsStatusLabel);
        statusPanel.Controls.Add(_audioStatusLabel);
        statusPanel.Controls.Add(_statsLabel);

        // 日志面板
        var logLabel = new Label
        {
            Text = "实时日志：",
            Dock = DockStyle.Top,
            Height = 25,
            ForeColor = Color.Gray,
            Padding = new Padding(10, 5, 0, 0),
        };

        _logTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 9),
            WordWrap = false,
        };

        // 底部按钮
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5),
        };

        var clearLogBtn = new Button { Text = "清空日志", Width = 80 };
        clearLogBtn.Click += (_, _) => _logTextBox.Clear();

        var openLogFileBtn = new Button { Text = "打开日志文件", Width = 100 };
        openLogFileBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FileLogger.Instance.GetLogFilePath(),
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开日志文件：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        buttonPanel.Controls.Add(clearLogBtn);
        buttonPanel.Controls.Add(openLogFileBtn);

        // 添加控件
        Controls.Add(_logTextBox);
        Controls.Add(logLabel);
        Controls.Add(statusPanel);
        Controls.Add(buttonPanel);

        // 订阅日志事件
        FileLogger.Instance.LogWritten += OnLogWritten;

        // 启动刷新定时器
        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 500, // 每 500ms 刷新一次
        };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();

        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            FileLogger.Instance.LogWritten -= OnLogWritten;
        };
    }

    public void SetDataSources(
        Func<AbpWebSocketServer?> getServer,
        Func<AudioBridgeService?> getAudioService,
        Func<string> getState)
    {
        _getServer = getServer;
        _getAudioService = getAudioService;
        _getState = getState;
    }

    private void OnLogWritten(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
        }
        else
        {
            AppendLog(line);
        }
    }

    private void AppendLog(string line)
    {
        // 限制日志行数
        if (_logTextBox.Lines.Length > 500)
        {
            var lines = _logTextBox.Lines.Skip(100).ToArray();
            _logTextBox.Lines = lines;
        }

        _logTextBox.AppendText(line + Environment.NewLine);
        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.ScrollToCaret();
    }

    private void RefreshStatus()
    {
        var state = _getState?.Invoke() ?? "未知";
        var server = _getServer?.Invoke();
        var audio = _getAudioService?.Invoke();

        // 状态标签
        _statusLabel.Text = $"状态：{state}";
        _statusLabel.ForeColor = state switch
        {
            "Listening" => Color.Yellow,
            "Connected" => Color.LightGreen,
            "Stopped" => Color.Gray,
            "Error" => Color.Red,
            _ => Color.White,
        };

        // WebSocket 状态
        if (server != null)
        {
            var wsRunning = server.IsRunning ? "✓ 运行中" : "✗ 已停止";
            var wsConnected = server.HasActiveSession ? "✓ 已连接" : "✗ 未连接";
            var codec = server.HandshakeDone ? server.SelectedCodec : "(handshake...)";
            _wsStatusLabel.Text = $"WebSocket：{wsRunning} | 端口 {server.Port} | Android {wsConnected} | codec {codec}";
            _wsStatusLabel.ForeColor = server.HasActiveSession ? Color.LightGreen : Color.LightGray;
        }
        else
        {
            _wsStatusLabel.Text = "WebSocket：未初始化";
            _wsStatusLabel.ForeColor = Color.Gray;
        }

        // 音频状态
        if (audio != null)
        {
            var audioStatus = audio.GetStatus();
            var loopback = audioStatus.IsLoopbackCapturing ? "✓" : "✗";
            var virtualMic = audioStatus.IsVirtualMicRendering ? "✓" : "✗";
            _audioStatusLabel.Text = $"音频：Loopback {loopback} | 虚拟麦克风 {virtualMic} | 缓冲 {audioStatus.VirtualMicBufferedMs}ms";
            _audioStatusLabel.ForeColor = audioStatus.IsRunning ? Color.LightGreen : Color.LightGray;

            // 统计
            var downlink = server?.DownlinkFramesSent ?? 0;
            var uplink = server?.UplinkFramesReceived ?? 0;
            var downBytes = server?.DownlinkPayloadBytesSent ?? 0;
            var upBytes = server?.UplinkPayloadBytesReceived ?? 0;
            var downSupp = server?.DownlinkFramesSuppressed ?? 0;
            _statsLabel.Text =
                $"统计：↓发送 {downlink} 帧({FormatBytes(downBytes)}) | ↓静音丢弃 {downSupp} 帧 | ↑接收 {uplink} 帧({FormatBytes(upBytes)}) | 写入虚拟麦 {audioStatus.VirtualMicFramesWritten} 帧 | 欠载 {audioStatus.VirtualMicUnderrunCount}";
        }
        else
        {
            _audioStatusLabel.Text = "音频：未初始化";
            _audioStatusLabel.ForeColor = Color.Gray;
            _statsLabel.Text = "统计：-";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-";
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0}KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.0}MB";
        return $"{bytes / (1024d * 1024 * 1024):0.00}GB";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
