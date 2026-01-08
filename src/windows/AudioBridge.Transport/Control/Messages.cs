using System.Text.Json.Serialization;

namespace AudioBridge.Transport.Control;

public sealed record HelloMessage(
    [property: JsonPropertyName("proto")] string Proto,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("cap")] HelloCapabilities Cap
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "hello";
}

public sealed record HelloCapabilities(
    [property: JsonPropertyName("codec")] string[] Codec,
    [property: JsonPropertyName("sampleRate")] int[] SampleRate,
    [property: JsonPropertyName("frameMs")] int[] FrameMs,
    [property: JsonPropertyName("uplink")] bool Uplink,
    [property: JsonPropertyName("downlink")] bool Downlink
);

public sealed record WelcomeMessage(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("selected")] SelectedConfig Selected,
    [property: JsonPropertyName("server")] ServerConfig Server
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "welcome";
}

public sealed record SelectedConfig(
    [property: JsonPropertyName("codec")] string Codec,
    [property: JsonPropertyName("sampleRate")] int SampleRate,
    [property: JsonPropertyName("channels")] int Channels,
    [property: JsonPropertyName("frameMs")] int FrameMs
);

public sealed record ServerConfig(
    [property: JsonPropertyName("heartbeatMs")] int HeartbeatMs
);

public sealed record PingMessage(
    [property: JsonPropertyName("t")] long T
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "ping";
}

public sealed record PongMessage(
    [property: JsonPropertyName("t")] long T
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "pong";
}

public sealed record ErrorMessage(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "error";
}

public sealed record PttMessage(
    [property: JsonPropertyName("enabled")] bool Enabled
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "ptt";
}

public sealed record MuteUplinkMessage(
    [property: JsonPropertyName("enabled")] bool Enabled
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "muteUplink";
}

public sealed record MuteDownlinkMessage(
    [property: JsonPropertyName("enabled")] bool Enabled
) : IAbpControlMessage
{
    [JsonPropertyName("type")]
    public string Type => "muteDownlink";
}

