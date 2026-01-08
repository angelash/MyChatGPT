namespace AudioBridge.Core.Devices;

/// <summary>
/// 音频设备信息
/// </summary>
public record AudioDeviceInfo(
    string Id,
    string FriendlyName,
    AudioDeviceType DeviceType,
    bool IsDefault
);

public enum AudioDeviceType
{
    /// <summary>播放设备（输出）</summary>
    Render,
    /// <summary>录制设备（输入）</summary>
    Capture
}
