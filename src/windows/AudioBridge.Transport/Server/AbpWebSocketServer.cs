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
/// 目前实现到：hello/welcome、ping/pong、binary frame 解码 + 音频转发。
/// </summary>
public sealed class AbpWebSocketServer : IAsyncDisposable
{
    private readonly int _port;
    private readonly string? _token;
    private WebApplication? _app;
    private WebSocket? _activeSession;
    private uint _downlinkSeq;
    private long _downlinkFramesSent;
    private long _uplinkFramesReceived;

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

    /// <summary>
    /// 当收到上行音频帧（从 Android 麦克风）时触发
    /// </summary>
    public event Action<byte[]>? UplinkFrameReceived;

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
            port = _port
        }));

        app.Map("/abp", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket required", cancellationToken);
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            await HandleSessionAsync(ws, app.Logger, cancellationToken);
        });

        await app.StartAsync(cancellationToken);
        _app = app;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null)
        {
            return;
        }

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
        _activeSession = ws;
        _downlinkSeq = 0;

        var buffer = new byte[64 * 1024];
        HelloMessage? hello = null;

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
                    var msg = AbpControlJson.Deserialize(json);

                    switch (msg)
                    {
                        case HelloMessage h:
                            hello = h;
                            if (!IsTokenOk(h.Token))
                            {
                                await SendTextAsync(ws, AbpControlJson.Serialize(new ErrorMessage("AUTH_FAIL", "invalid token")), ct);
                                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "AUTH_FAIL", ct);
                                return;
                            }

                            // v1：先固定选 PCM（后续可按 cap 选择 Opus）
                            var welcome = new WelcomeMessage(
                                SessionId: Guid.NewGuid().ToString("N"),
                                Selected: new SelectedConfig(Codec: "pcm", SampleRate: 48000, Channels: 1, FrameMs: 20),
                                Server: new ServerConfig(HeartbeatMs: 5000));

                            await SendTextAsync(ws, AbpControlJson.Serialize(welcome), ct);
                            break;

                        case PingMessage ping:
                            await SendTextAsync(ws, AbpControlJson.Serialize(new PongMessage(ping.T)), ct);
                            break;

                        default:
                            // 其他控制消息 v1 先忽略（ptt/mute 等）
                            break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    if (!AbpBinaryFrame.TryDecode(messageBytes, out var frame, out var error))
                    {
                        logger.LogWarning("Bad binary frame: {Error}", error);
                        continue;
                    }

                    // 上行音频帧（从 Android 麦克风）
                    if (frame.StreamId == AbpStreamId.Uplink)
                    {
                        Interlocked.Increment(ref _uplinkFramesReceived);
                        UplinkFrameReceived?.Invoke(frame.Payload.ToArray());
                    }

                    _ = hello; // placeholder for future session binding
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (WebSocketException ex)
        {
            logger.LogInformation(ex, "WebSocket session ended");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WebSocket session error");
        }
        finally
        {
            if (_activeSession == ws)
            {
                _activeSession = null;
            }

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch
                {
                    // ignore
                }
            }
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
        catch
        {
            // 发送失败，忽略（可能连接已断开）
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

