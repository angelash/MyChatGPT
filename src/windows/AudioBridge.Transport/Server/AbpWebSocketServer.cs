using System.Net.WebSockets;
using System.Text;
using AudioBridge.Transport.Control;
using AudioBridge.Transport.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioBridge.Transport.Server;

/// <summary>
/// v1 WebSocket 单连接承载：控制(JSON text) + 音频帧(binary)。
/// </summary>
public sealed class AbpWebSocketServer : IAsyncDisposable
{
    private readonly int _port;
    private readonly string? _token;
    private WebApplication? _app;
    private WebSocket? _activeSession;
    private string? _activeDeviceId;
    private uint _downlinkSeq;
    private long _downlinkFramesSent;
    private long _uplinkFramesReceived;
    private long _pingCount;
    private DateTime _lastPingTime;

    /// <summary>日志回调（可选）</summary>
    public Action<string, string>? OnLog { get; set; }

    public AbpWebSocketServer(int port, string? token = null)
    {
        _port = port;
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public int Port => _port;
    public string? Token => _token;

    public bool IsRunning => _app is not null;
    public bool HasActiveSession => _activeSession?.State == WebSocketState.Open;
    public long DownlinkFramesSent => _downlinkFramesSent;
    public long UplinkFramesReceived => _uplinkFramesReceived;
    public long PingCount => _pingCount;

    /// <summary>当收到上行音频帧（从 Android 麦克风）时触发</summary>
    public event Action<byte[]>? UplinkFrameReceived;

    /// <summary>当客户端连接成功时触发</summary>
    public event Action<string>? SessionConnected;

    /// <summary>当客户端断开时触发</summary>
    public event Action<string, string>? SessionDisconnected;

    private void Log(string level, string message)
    {
        OnLog?.Invoke(level, message);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        // 根路径：返回服务状态（方便浏览器测试）
        app.MapGet("/", () => Results.Ok(new
        {
            service = "AudioBridge",
            status = "running",
            wsEndpoint = "/abp",
            port = _port,
            hasActiveSession = HasActiveSession,
        }));

        app.Map("/abp", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket required", cancellationToken);
                return;
            }

            Log("INFO", $"WebSocket 连接请求来自 {context.Connection.RemoteIpAddress}");

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            await HandleSessionAsync(ws, app.Logger, cancellationToken);
        });

        await app.StartAsync(cancellationToken);
        _app = app;
        Log("INFO", $"WebSocket 服务器已启动，端口 {_port}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null)
        {
            return;
        }

        Log("INFO", "正在停止 WebSocket 服务器...");
        await _app.StopAsync(cancellationToken);
        await _app.DisposeAsync();
        _app = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task HandleSessionAsync(WebSocket ws, ILogger logger, CancellationToken ct)
    {
        // v1: 单连接模式，后连接的会挤掉前一个
        var oldSession = _activeSession;
        _activeSession = ws;
        _downlinkSeq = 0;

        if (oldSession != null && oldSession.State == WebSocketState.Open)
        {
            Log("WARN", "新连接将挤掉旧连接");
            try
            {
                await oldSession.CloseAsync(WebSocketCloseStatus.PolicyViolation, "replaced", CancellationToken.None);
            }
            catch { }
        }

        var buffer = new byte[64 * 1024];
        HelloMessage? hello = null;
        string disconnectReason = "unknown";

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        disconnectReason = $"客户端主动关闭: {result.CloseStatus} - {result.CloseStatusDescription}";
                        Log("INFO", disconnectReason);
                        return;
                    }

                    if (result.Count > 0)
                    {
                        ms.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                var messageBytes = ms.ToArray();

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(messageBytes);
                    Log("DEBUG", $"收到文本消息: {json}");

                    var msg = AbpControlJson.Deserialize(json);

                    switch (msg)
                    {
                        case HelloMessage h:
                            hello = h;
                            _activeDeviceId = h.DeviceId;
                            Log("INFO", $"收到 Hello: DeviceId={h.DeviceId}");

                            if (!IsTokenOk(h.Token))
                            {
                                Log("WARN", "Token 验证失败");
                                await SendTextAsync(ws, AbpControlJson.Serialize(new ErrorMessage("AUTH_FAIL", "invalid token")), ct);
                                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "AUTH_FAIL", ct);
                                disconnectReason = "Token 验证失败";
                                return;
                            }

                            // v1：先固定选 PCM
                            var welcome = new WelcomeMessage(
                                SessionId: Guid.NewGuid().ToString("N"),
                                Selected: new SelectedConfig(Codec: "pcm", SampleRate: 48000, Channels: 1, FrameMs: 20),
                                Server: new ServerConfig(HeartbeatMs: 5000));

                            await SendTextAsync(ws, AbpControlJson.Serialize(welcome), ct);
                            Log("INFO", $"已发送 Welcome，SessionId={welcome.SessionId}");

                            // 触发连接事件
                            SessionConnected?.Invoke(h.DeviceId);
                            break;

                        case PingMessage ping:
                            Interlocked.Increment(ref _pingCount);
                            _lastPingTime = DateTime.Now;
                            await SendTextAsync(ws, AbpControlJson.Serialize(new PongMessage(ping.T)), ct);
                            Log("DEBUG", $"Ping/Pong: t={ping.T}");
                            break;

                        default:
                            Log("DEBUG", $"收到其他控制消息: {msg?.GetType().Name}");
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    if (!AbpBinaryFrame.TryDecode(messageBytes, out var frame, out var error))
                    {
                        Log("WARN", $"二进制帧解码失败: {error}");
                        continue;
                    }

                    // 上行音频帧（从 Android 麦克风）
                    if (frame.StreamId == AbpStreamId.Uplink)
                    {
                        Interlocked.Increment(ref _uplinkFramesReceived);
                        UplinkFrameReceived?.Invoke(frame.Payload.ToArray());
                    }
                }
            }

            disconnectReason = $"WebSocket 状态变为 {ws.State}";
        }
        catch (OperationCanceledException)
        {
            disconnectReason = "操作被取消";
        }
        catch (WebSocketException ex)
        {
            disconnectReason = $"WebSocket 异常: {ex.Message}";
            Log("WARN", disconnectReason);
        }
        catch (Exception ex)
        {
            disconnectReason = $"未知异常: {ex.Message}";
            Log("ERROR", $"会话异常: {ex}");
        }
        finally
        {
            var deviceId = _activeDeviceId ?? "unknown";
            if (_activeSession == ws)
            {
                _activeSession = null;
                _activeDeviceId = null;
            }

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch { }
            }

            Log("INFO", $"会话结束: {deviceId}, 原因: {disconnectReason}");
            SessionDisconnected?.Invoke(deviceId, disconnectReason);
        }
    }

    private bool IsTokenOk(string? clientToken)
    {
        if (_token is null)
        {
            return true;
        }

        return string.Equals(_token, clientToken, StringComparison.Ordinal);
    }

    private static Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    /// <summary>
    /// 发送下行音频帧（系统声音 -> Android）
    /// </summary>
    public async Task SendDownlinkFrameAsync(byte[] pcmPayload, uint timestampSamples = 0)
    {
        var ws = _activeSession;
        if (ws is null || ws.State != WebSocketState.Open)
        {
            return;
        }

        var seq = Interlocked.Increment(ref _downlinkSeq);
        var frame = new AbpBinaryFrame(AbpStreamId.Downlink, seq, timestampSamples, pcmPayload);
        var bytes = frame.Encode();

        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
            Interlocked.Increment(ref _downlinkFramesSent);
        }
        catch (Exception ex)
        {
            Log("WARN", $"发送下行帧失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送下行音频帧（同步版本，用于高频调用）
    /// </summary>
    public void SendDownlinkFrame(byte[] pcmPayload, uint timestampSamples = 0)
    {
        _ = SendDownlinkFrameAsync(pcmPayload, timestampSamples);
    }
}
