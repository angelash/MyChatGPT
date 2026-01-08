using NAudio.CoreAudioApi;

namespace AudioBridge.Core.Devices;

/// <summary>
/// 音频设备管理器：枚举、匹配、监听设备变化
/// </summary>
public class DeviceManager : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;

    /// <summary>虚拟声卡名称关键词（用于自动匹配）</summary>
    public static readonly string[] VirtualCableHints = ["CABLE", "Virtual Cable", "VB-Audio"];

    public DeviceManager()
    {
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// 获取所有播放设备
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        return GetDevices(DataFlow.Render);
    }

    /// <summary>
    /// 获取所有录制设备
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        return GetDevices(DataFlow.Capture);
    }

    /// <summary>
    /// 按设备 ID 获取 MMDevice（用于 NAudio 操作）
    /// </summary>
    public MMDevice? GetDeviceById(string deviceId)
    {
        try
        {
            return _enumerator.GetDevice(deviceId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取默认播放设备
    /// </summary>
    public MMDevice? GetDefaultRenderDevice()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取默认录制设备
    /// </summary>
    public MMDevice? GetDefaultCaptureDevice()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 按名称关键词查找播放设备（用于匹配虚拟声卡）
    /// </summary>
    public MMDevice? FindRenderDeviceByName(params string[] keywords)
    {
        return FindDeviceByName(DataFlow.Render, keywords);
    }

    /// <summary>
    /// 按名称关键词查找录制设备
    /// </summary>
    public MMDevice? FindCaptureDeviceByName(params string[] keywords)
    {
        return FindDeviceByName(DataFlow.Capture, keywords);
    }

    /// <summary>
    /// 自动查找 VB-CABLE 的 Playback 端（CABLE Input）
    /// </summary>
    public MMDevice? FindVirtualCablePlayback()
    {
        // CABLE Input 是播放设备（我们往里写音频）
        return FindRenderDeviceByName("CABLE Input");
    }

    /// <summary>
    /// 自动查找 VB-CABLE 的 Recording 端（CABLE Output）
    /// </summary>
    public MMDevice? FindVirtualCableRecording()
    {
        // CABLE Output 是录制设备（浏览器从这里读）
        return FindCaptureDeviceByName("CABLE Output");
    }

    /// <summary>
    /// 检查设备是否为 Remote Audio（RDP 注入的设备）
    /// </summary>
    public static bool IsRemoteAudioDevice(string friendlyName)
    {
        return friendlyName.Contains("Remote Audio", StringComparison.OrdinalIgnoreCase)
            || friendlyName.Contains("远程音频", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<AudioDeviceInfo> GetDevices(DataFlow dataFlow)
    {
        var result = new List<AudioDeviceInfo>();

        MMDevice? defaultDevice = null;
        try
        {
            defaultDevice = _enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
        }
        catch { /* 可能没有默认设备 */ }

        var devices = _enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        foreach (var device in devices)
        {
            var info = new AudioDeviceInfo(
                Id: device.ID,
                FriendlyName: device.FriendlyName,
                DeviceType: dataFlow == DataFlow.Render ? AudioDeviceType.Render : AudioDeviceType.Capture,
                IsDefault: defaultDevice != null && device.ID == defaultDevice.ID
            );
            result.Add(info);
        }

        return result;
    }

    private MMDevice? FindDeviceByName(DataFlow dataFlow, params string[] keywords)
    {
        var devices = _enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        foreach (var device in devices)
        {
            var name = device.FriendlyName;
            foreach (var keyword in keywords)
            {
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enumerator.Dispose();
    }
}
