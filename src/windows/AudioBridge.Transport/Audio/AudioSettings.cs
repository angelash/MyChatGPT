namespace AudioBridge.Transport.Audio;

/// <summary>
/// 音频桥接全局设置（支持动态修改）
/// </summary>
public sealed class AudioSettings
{
    private static readonly Lazy<AudioSettings> _instance = new(() => new AudioSettings());
    public static AudioSettings Instance => _instance.Value;

    private AudioSettings() { }

    // ===== 下行静音门设置（系统声音 -> Android）=====
    // 这些参数可以动态修改，立即生效

    private int _downlinkThresholdAvgAbs = 120;
    /// <summary>
    /// 下行静音门阈值（0-32767）。
    /// 低于此值的帧被视为静音。值越大，过滤越激进。
    /// 推荐：120（安静环境）、300-500（嘈杂环境）
    /// </summary>
    public int DownlinkThresholdAvgAbs
    {
        get => _downlinkThresholdAvgAbs;
        set
        {
            _downlinkThresholdAvgAbs = Math.Clamp(value, 0, 32767);
            SettingsChanged?.Invoke();
        }
    }

    private int _downlinkMinSilentFrames = 10;
    /// <summary>
    /// 下行静音门最小静音帧数（连续多少帧静音后才开始丢弃）。
    /// 值越大，静音后延迟越久才停止发送（hangover）。
    /// 推荐：5-20
    /// </summary>
    public int DownlinkMinSilentFrames
    {
        get => _downlinkMinSilentFrames;
        set
        {
            _downlinkMinSilentFrames = Math.Clamp(value, 1, 100);
            SettingsChanged?.Invoke();
        }
    }

    // ===== 上行静音门设置（Android 麦克风 -> Windows）=====
    // 注意：这些是 Android 端的参数，需要通过协议同步，或由用户在 Android 端设置
    // 这里仅作为"建议值"存储，实际需要协议支持才能同步

    private int _uplinkThresholdAvgAbs = 120;
    /// <summary>
    /// 上行静音门阈值（Android 端使用）。
    /// 用于过滤环境噪音。值越大，过滤越激进，但可能丢失轻声说话。
    /// 推荐：200-500（嘈杂环境）、100-150（安静环境）
    /// </summary>
    public int UplinkThresholdAvgAbs
    {
        get => _uplinkThresholdAvgAbs;
        set
        {
            _uplinkThresholdAvgAbs = Math.Clamp(value, 0, 32767);
            SettingsChanged?.Invoke();
        }
    }

    private int _uplinkMinSilentFrames = 10;
    /// <summary>
    /// 上行静音门最小静音帧数（Android 端使用）。
    /// </summary>
    public int UplinkMinSilentFrames
    {
        get => _uplinkMinSilentFrames;
        set
        {
            _uplinkMinSilentFrames = Math.Clamp(value, 1, 100);
            SettingsChanged?.Invoke();
        }
    }

    // ===== 需要重连才能生效的设置 =====

    private int _wsPort = 21347;
    /// <summary>
    /// WebSocket 服务器端口。修改后需要重启服务。
    /// </summary>
    public int WsPort
    {
        get => _wsPort;
        set
        {
            if (_wsPort != value)
            {
                _wsPort = Math.Clamp(value, 1024, 65535);
                RestartRequiredSettingsChanged?.Invoke();
            }
        }
    }

    private string? _wsToken;
    /// <summary>
    /// WebSocket 认证 Token（可选）。修改后需要重启服务。
    /// </summary>
    public string? WsToken
    {
        get => _wsToken;
        set
        {
            if (_wsToken != value)
            {
                _wsToken = string.IsNullOrWhiteSpace(value) ? null : value;
                RestartRequiredSettingsChanged?.Invoke();
            }
        }
    }

    // ===== 事件 =====

    /// <summary>
    /// 当可动态生效的设置改变时触发
    /// </summary>
    public event Action? SettingsChanged;

    /// <summary>
    /// 当需要重启才能生效的设置改变时触发
    /// </summary>
    public event Action? RestartRequiredSettingsChanged;

    // ===== 辅助方法 =====

    /// <summary>
    /// 重置所有设置为默认值
    /// </summary>
    public void ResetToDefaults()
    {
        _downlinkThresholdAvgAbs = 120;
        _downlinkMinSilentFrames = 10;
        _uplinkThresholdAvgAbs = 120;
        _uplinkMinSilentFrames = 10;
        _wsPort = 21347;
        _wsToken = null;
        SettingsChanged?.Invoke();
        RestartRequiredSettingsChanged?.Invoke();
    }

    /// <summary>
    /// 获取当前设置的摘要信息
    /// </summary>
    public string GetSummary()
    {
        return $"下行阈值={_downlinkThresholdAvgAbs}, 下行静音帧数={_downlinkMinSilentFrames}, " +
               $"上行阈值={_uplinkThresholdAvgAbs}, 上行静音帧数={_uplinkMinSilentFrames}, " +
               $"端口={_wsPort}";
    }
}
