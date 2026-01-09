using AudioBridge.Transport.Audio;
using AudioBridge.Transport.Server;

namespace AudioBridge.Agent.Tray;

/// <summary>
/// 音频参数设置窗体
/// </summary>
public class SettingsForm : Form
{
    private readonly AudioSettings _settings = AudioSettings.Instance;

    // 动态生效组
    private NumericUpDown _downlinkThresholdInput = null!;
    private NumericUpDown _downlinkMinSilentFramesInput = null!;
    private NumericUpDown _uplinkThresholdInput = null!;
    private NumericUpDown _uplinkMinSilentFramesInput = null!;

    // 需要重连生效组
    private NumericUpDown _wsPortInput = null!;
    private TextBox _wsTokenInput = null!;

    // 实时监控
    private Label _downlinkLevelLabel = null!;
    private ProgressBar _downlinkLevelBar = null!;
    private Label _downlinkStatusLabel = null!;

    private Func<AbpWebSocketServer?>? _getServer;
    private readonly System.Windows.Forms.Timer _monitorTimer;

    public SettingsForm()
    {
        InitializeComponent();

        // 启动监控定时器
        _monitorTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _monitorTimer.Tick += (_, _) => UpdateMonitor();
    }

    public void SetDataSource(Func<AbpWebSocketServer?> getServer)
    {
        _getServer = getServer;
    }

    private void InitializeComponent()
    {
        Text = "AudioBridge 参数设置";
        Size = new Size(550, 580);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15),
            AutoScroll = true,
        };

        int y = 10;

        // ===== 动态生效组 =====
        var dynamicGroup = CreateGroupBox("🟢 动态生效（实时应用，无需重连）", 10, y, 500, 250);
        y += 260;

        int gy = 25;

        // 下行阈值
        AddLabelAndInput(dynamicGroup, "下行静音阈值 (系统声→手机):", ref gy,
            "低于此值的音频帧被视为静音。\n推荐：120（安静）、300-500（嘈杂）",
            0, 32767, _settings.DownlinkThresholdAvgAbs, out _downlinkThresholdInput);
        _downlinkThresholdInput.ValueChanged += (_, _) =>
        {
            _settings.DownlinkThresholdAvgAbs = (int)_downlinkThresholdInput.Value;
            ApplyToServer();
        };

        // 下行静音帧数
        AddLabelAndInput(dynamicGroup, "下行静音帧数 (hangover):", ref gy,
            "连续多少帧静音后才开始丢弃。\n值越大，结束延迟越久。推荐：5-20",
            1, 100, _settings.DownlinkMinSilentFrames, out _downlinkMinSilentFramesInput);
        _downlinkMinSilentFramesInput.ValueChanged += (_, _) =>
        {
            _settings.DownlinkMinSilentFrames = (int)_downlinkMinSilentFramesInput.Value;
            ApplyToServer();
        };

        gy += 10;

        // 上行阈值（Android 端）
        AddLabelAndInput(dynamicGroup, "上行静音阈值 (手机麦克风→电脑):", ref gy,
            "用于过滤环境噪音。推荐：200-500（嘈杂）、100-150（安静）",
            0, 32767, _settings.UplinkThresholdAvgAbs, out _uplinkThresholdInput);
        _uplinkThresholdInput.ValueChanged += (_, _) =>
        {
            _settings.UplinkThresholdAvgAbs = (int)_uplinkThresholdInput.Value;
        };

        // 上行静音帧数（Android 端）
        AddLabelAndInput(dynamicGroup, "上行静音帧数 (Android hangover):", ref gy,
            "连续多少帧静音后才停止发送。",
            1, 100, _settings.UplinkMinSilentFrames, out _uplinkMinSilentFramesInput);
        _uplinkMinSilentFramesInput.ValueChanged += (_, _) =>
        {
            _settings.UplinkMinSilentFrames = (int)_uplinkMinSilentFramesInput.Value;
        };

        gy += 5;

        // 同步到 Android 按钮
        var syncBtn = new Button
        {
            Text = "📤 同步上行参数到 Android",
            Location = new Point(15, gy),
            Size = new Size(200, 28),
            FlatStyle = FlatStyle.Flat,
        };
        syncBtn.Click += (_, _) => SyncToAndroid();
        dynamicGroup.Controls.Add(syncBtn);

        var syncTip = new Label
        {
            Text = "点击后推送上行参数到已连接的 Android",
            Location = new Point(220, gy + 5),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, 8),
        };
        dynamicGroup.Controls.Add(syncTip);

        mainPanel.Controls.Add(dynamicGroup);

        // ===== 实时监控组 =====
        var monitorGroup = CreateGroupBox("📊 实时监控", 10, y, 500, 100);
        y += 110;

        _downlinkLevelLabel = new Label
        {
            Text = "下行音量：-",
            Location = new Point(15, 25),
            AutoSize = true,
            Font = new Font("Consolas", 10),
        };
        monitorGroup.Controls.Add(_downlinkLevelLabel);

        _downlinkLevelBar = new ProgressBar
        {
            Location = new Point(15, 50),
            Size = new Size(460, 20),
            Maximum = 5000,
            Style = ProgressBarStyle.Continuous,
        };
        monitorGroup.Controls.Add(_downlinkLevelBar);

        _downlinkStatusLabel = new Label
        {
            Text = "状态：-",
            Location = new Point(15, 75),
            AutoSize = true,
            ForeColor = Color.Gray,
        };
        monitorGroup.Controls.Add(_downlinkStatusLabel);

        mainPanel.Controls.Add(monitorGroup);

        // ===== 需要重连生效组 =====
        var restartGroup = CreateGroupBox("🔴 需要重启服务才能生效", 10, y, 500, 120);
        y += 130;

        int rgy = 25;

        // WebSocket 端口
        AddLabelAndInput(restartGroup, "WebSocket 端口:", ref rgy,
            "服务监听端口。修改后需要重启服务。",
            1024, 65535, _settings.WsPort, out _wsPortInput);
        _wsPortInput.ValueChanged += (_, _) => _settings.WsPort = (int)_wsPortInput.Value;

        // Token
        var tokenLabel = new Label
        {
            Text = "认证 Token (可选):",
            Location = new Point(15, rgy),
            AutoSize = true,
        };
        restartGroup.Controls.Add(tokenLabel);

        _wsTokenInput = new TextBox
        {
            Location = new Point(200, rgy - 3),
            Size = new Size(200, 23),
            Text = _settings.WsToken ?? "",
            PlaceholderText = "留空则不验证",
        };
        _wsTokenInput.TextChanged += (_, _) => _settings.WsToken = _wsTokenInput.Text;
        restartGroup.Controls.Add(_wsTokenInput);

        var tokenTip = new Label
        {
            Text = "⚠️ 修改后需要重启服务",
            Location = new Point(410, rgy),
            AutoSize = true,
            ForeColor = Color.OrangeRed,
            Font = new Font(Font, FontStyle.Italic),
        };
        restartGroup.Controls.Add(tokenTip);

        mainPanel.Controls.Add(restartGroup);

        // ===== 底部按钮 =====
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 5, 10, 5),
        };

        var resetBtn = new Button
        {
            Text = "恢复默认",
            Width = 90,
            Height = 30,
        };
        resetBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("确定要恢复所有设置为默认值吗？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                ResetToDefaults();
            }
        };

        var closeBtn = new Button
        {
            Text = "关闭",
            Width = 80,
            Height = 30,
        };
        closeBtn.Click += (_, _) => Hide();

        buttonPanel.Controls.Add(closeBtn);
        buttonPanel.Controls.Add(resetBtn);

        Controls.Add(mainPanel);
        Controls.Add(buttonPanel);
    }

    private static GroupBox CreateGroupBox(string text, int x, int y, int width, int height)
    {
        return new GroupBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
        };
    }

    private static void AddLabelAndInput(Control parent, string labelText, ref int y,
        string tooltip, int min, int max, int value, out NumericUpDown input)
    {
        var label = new Label
        {
            Text = labelText,
            Location = new Point(15, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
        };
        parent.Controls.Add(label);

        input = new NumericUpDown
        {
            Location = new Point(280, y - 3),
            Size = new Size(100, 23),
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Font = new Font("Consolas", 10),
        };
        parent.Controls.Add(input);

        var tip = new ToolTip();
        tip.SetToolTip(label, tooltip);
        tip.SetToolTip(input, tooltip);

        y += 30;
    }

    private void ApplyToServer()
    {
        var server = _getServer?.Invoke();
        if (server?.DownlinkSilenceGate != null)
        {
            server.DownlinkSilenceGate.ThresholdAvgAbs = _settings.DownlinkThresholdAvgAbs;
            server.DownlinkSilenceGate.MinSilentFramesToSuppress = _settings.DownlinkMinSilentFrames;
        }
    }

    private void SyncToAndroid()
    {
        var server = _getServer?.Invoke();
        if (server == null || !server.HasActiveSession)
        {
            MessageBox.Show("没有已连接的 Android 客户端。", "同步失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        server.SendConfig(
            uplinkThreshold: _settings.UplinkThresholdAvgAbs,
            uplinkMinSilentFrames: _settings.UplinkMinSilentFrames);

        MessageBox.Show(
            $"已发送配置到 Android：\n" +
            $"• 上行阈值：{_settings.UplinkThresholdAvgAbs}\n" +
            $"• 上行静音帧数：{_settings.UplinkMinSilentFrames}",
            "同步成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UpdateMonitor()
    {
        var server = _getServer?.Invoke();
        var gate = server?.DownlinkSilenceGate;

        if (gate != null)
        {
            var level = gate.LastAvgAbs;
            var threshold = gate.ThresholdAvgAbs;
            var suppressing = gate.IsSuppressing;
            var silentRun = gate.SilentRunFrames;

            _downlinkLevelLabel.Text = $"下行音量：{level,5} / 阈值 {threshold}";
            _downlinkLevelBar.Value = Math.Min(level, _downlinkLevelBar.Maximum);

            if (suppressing)
            {
                _downlinkStatusLabel.Text = $"状态：静音中（已连续 {silentRun} 帧）- 不发送";
                _downlinkStatusLabel.ForeColor = Color.Gray;
            }
            else if (level < threshold)
            {
                _downlinkStatusLabel.Text = $"状态：低于阈值（连续 {silentRun} 帧）- 发送中";
                _downlinkStatusLabel.ForeColor = Color.Orange;
            }
            else
            {
                _downlinkStatusLabel.Text = "状态：有声音 - 发送中";
                _downlinkStatusLabel.ForeColor = Color.Green;
            }
        }
        else
        {
            _downlinkLevelLabel.Text = "下行音量：服务未运行";
            _downlinkLevelBar.Value = 0;
            _downlinkStatusLabel.Text = "状态：-";
            _downlinkStatusLabel.ForeColor = Color.Gray;
        }
    }

    private void ResetToDefaults()
    {
        _settings.ResetToDefaults();

        _downlinkThresholdInput.Value = _settings.DownlinkThresholdAvgAbs;
        _downlinkMinSilentFramesInput.Value = _settings.DownlinkMinSilentFrames;
        _uplinkThresholdInput.Value = _settings.UplinkThresholdAvgAbs;
        _uplinkMinSilentFramesInput.Value = _settings.UplinkMinSilentFrames;
        _wsPortInput.Value = _settings.WsPort;
        _wsTokenInput.Text = _settings.WsToken ?? "";

        ApplyToServer();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _monitorTimer.Start();
            // 同步当前设置到 UI
            _downlinkThresholdInput.Value = _settings.DownlinkThresholdAvgAbs;
            _downlinkMinSilentFramesInput.Value = _settings.DownlinkMinSilentFrames;
            _uplinkThresholdInput.Value = _settings.UplinkThresholdAvgAbs;
            _uplinkMinSilentFramesInput.Value = _settings.UplinkMinSilentFrames;
            _wsPortInput.Value = _settings.WsPort;
            _wsTokenInput.Text = _settings.WsToken ?? "";

            // 同步服务器当前值
            var server = _getServer?.Invoke();
            if (server?.DownlinkSilenceGate != null)
            {
                _downlinkThresholdInput.Value = server.DownlinkSilenceGate.ThresholdAvgAbs;
                _downlinkMinSilentFramesInput.Value = server.DownlinkSilenceGate.MinSilentFramesToSuppress;
            }
        }
        else
        {
            _monitorTimer.Stop();
        }
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
