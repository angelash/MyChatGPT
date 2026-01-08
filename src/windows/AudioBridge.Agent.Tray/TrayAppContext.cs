using System.Drawing;
using AudioBridge.Transport.Server;

namespace AudioBridge.Agent.Tray;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startMenuItem;
    private readonly ToolStripMenuItem _stopMenuItem;
    private TrayState _state = TrayState.Stopped;
    private AbpWebSocketServer? _server;

    public TrayAppContext()
    {
        _startMenuItem = new ToolStripMenuItem("Start", null, (_, _) => Start());
        _stopMenuItem = new ToolStripMenuItem("Stop", null, (_, _) => Stop()) { Enabled = false };

        var showStatusItem = new ToolStripMenuItem("Show Status", null, (_, _) => ShowStatus());
        var openDocsItem = new ToolStripMenuItem("Open Docs", null, (_, _) => OpenDocs());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showStatusItem);
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
        MessageBox.Show(
            $"状态：{_state}\n\n提示：v1 默认建议戴耳机使用（避免自激）。",
            "AudioBridge Status",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
            // TODO: 从 settings.json 读取端口/token；目前先硬编码默认端口
            _server ??= new AbpWebSocketServer(port: 21347, token: null);
            await _server.StartAsync();

            _state = TrayState.Listening;
            UpdateUiForState();
            _notifyIcon.ShowBalloonTip(1500, "AudioBridge", $"已启动：监听 {_server.Port}，等待手机连接…", ToolTipIcon.Info);
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
            if (_server is not null)
            {
                await _server.StopAsync();
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

