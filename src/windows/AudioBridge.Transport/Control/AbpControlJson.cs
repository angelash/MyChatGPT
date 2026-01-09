using System.Text.Json;

namespace AudioBridge.Transport.Control;

public static class AbpControlJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize<T>(T message) where T : IAbpControlMessage
    {
        return JsonSerializer.Serialize(message, Options);
    }

    public static IAbpControlMessage Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp))
        {
            throw new JsonException("missing field: type");
        }

        var type = typeProp.GetString();
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new JsonException("invalid field: type");
        }

        return type switch
        {
            "hello" => JsonSerializer.Deserialize<HelloMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize hello"),
            "welcome" => JsonSerializer.Deserialize<WelcomeMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize welcome"),
            "ping" => JsonSerializer.Deserialize<PingMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize ping"),
            "pong" => JsonSerializer.Deserialize<PongMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize pong"),
            "error" => JsonSerializer.Deserialize<ErrorMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize error"),
            "ptt" => JsonSerializer.Deserialize<PttMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize ptt"),
            "muteUplink" => JsonSerializer.Deserialize<MuteUplinkMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize muteUplink"),
            "muteDownlink" => JsonSerializer.Deserialize<MuteDownlinkMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize muteDownlink"),
            "config" => JsonSerializer.Deserialize<ConfigMessage>(json, Options)
                ?? throw new JsonException("failed to deserialize config"),
            _ => throw new JsonException($"unknown message type: {type}"),
        };
    }
}

