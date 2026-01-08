using AudioBridge.Core.Devices;
using NAudio.CoreAudioApi;

namespace AudioBridge.Core.Audio;

/// <summary>
/// 音频桥接服务：协调 Loopback 捕获和虚拟麦克风渲染
/// </summary>
public class AudioBridgeService : IDisposable
{
    private readonly DeviceManager _deviceManager;
    private readonly LoopbackCapture _loopbackCapture;
    private readonly VirtualMicRenderer _virtualMicRenderer;
    private bool _disposed;
    private bool _isRunning;

    // 设备配置
    private string? _loopbackDeviceId;
    private string? _virtualMicDeviceId;

    /// <summary>当前是否正在运行</summary>
    public bool IsRunning => _isRunning;

    /// <summary>Loopback 捕获器</summary>
    public LoopbackCapture LoopbackCapture => _loopbackCapture;

    /// <summary>虚拟麦克风渲染器</summary>
    public VirtualMicRenderer VirtualMicRenderer => _virtualMicRenderer;

    /// <summary>设备管理器</summary>
    public DeviceManager DeviceManager => _deviceManager;

    /// <summary>
    /// 当收到下行音频帧（系统声音）时触发，用于发送给 Android
    /// </summary>
    public event Action<byte[]>? DownlinkFrameAvailable;

    /// <summary>
    /// 当发生错误时触发
    /// </summary>
    public event Action<string, Exception?>? Error;

    public AudioBridgeService()
    {
        _deviceManager = new DeviceManager();
        _loopbackCapture = new LoopbackCapture();
        _virtualMicRenderer = new VirtualMicRenderer();

        // 连接 Loopback 输出到下行事件
        _loopbackCapture.FrameAvailable += frame => DownlinkFrameAvailable?.Invoke(frame);
        _loopbackCapture.Error += ex => Error?.Invoke("LoopbackCapture", ex);
    }

    /// <summary>
    /// 配置设备（可选，不配置则自动选择）
    /// </summary>
    public void Configure(string? loopbackDeviceId = null, string? virtualMicDeviceId = null)
    {
        _loopbackDeviceId = loopbackDeviceId;
        _virtualMicDeviceId = virtualMicDeviceId;
    }

    /// <summary>
    /// 启动音频桥接
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        // 查找/验证设备
        var loopbackDevice = GetLoopbackDevice();
        var virtualMicDevice = GetVirtualMicDevice();

        if (virtualMicDevice == null)
        {
            Error?.Invoke("找不到虚拟声卡 (CABLE Input)，请先安装 VB-Audio Virtual Cable", null);
            return;
        }

        // 启动 Loopback 捕获（抓系统声）
        _loopbackCapture.Start(loopbackDevice);

        // 启动虚拟麦克风渲染（写入 CABLE Input）
        _virtualMicRenderer.Start(virtualMicDevice);

        _isRunning = true;
    }

    /// <summary>
    /// 停止音频桥接
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _loopbackCapture.Stop();
        _virtualMicRenderer.Stop();

        _isRunning = false;
    }

    /// <summary>
    /// 写入上行音频帧（来自 Android 的麦克风数据）
    /// </summary>
    public void WriteUplinkFrame(byte[] pcmFrame)
    {
        if (!_isRunning) return;
        _virtualMicRenderer.WriteFrame(pcmFrame);
    }

    /// <summary>
    /// 获取状态信息
    /// </summary>
    public AudioBridgeStatus GetStatus()
    {
        return new AudioBridgeStatus(
            IsRunning: _isRunning,
            IsLoopbackCapturing: _loopbackCapture.IsCapturing,
            IsVirtualMicRendering: _virtualMicRenderer.IsRendering,
            VirtualMicBufferedMs: _virtualMicRenderer.BufferedMs,
            VirtualMicUnderrunCount: _virtualMicRenderer.UnderrunCount,
            VirtualMicFramesWritten: _virtualMicRenderer.FramesWritten
        );
    }

    private MMDevice? GetLoopbackDevice()
    {
        // 如果指定了设备 ID，优先使用
        if (!string.IsNullOrEmpty(_loopbackDeviceId))
        {
            var device = _deviceManager.GetDeviceById(_loopbackDeviceId);
            if (device != null) return device;
        }

        // 否则使用默认播放设备
        var defaultDevice = _deviceManager.GetDefaultRenderDevice();

        // 检查是否被 RDP 劫持
        if (defaultDevice != null && DeviceManager.IsRemoteAudioDevice(defaultDevice.FriendlyName))
        {
            Error?.Invoke("默认播放设备是 Remote Audio（可能被 RDP 切换），建议手动指定设备", null);
        }

        return defaultDevice;
    }

    private MMDevice? GetVirtualMicDevice()
    {
        // 如果指定了设备 ID，优先使用
        if (!string.IsNullOrEmpty(_virtualMicDeviceId))
        {
            var device = _deviceManager.GetDeviceById(_virtualMicDeviceId);
            if (device != null) return device;
        }

        // 自动查找 CABLE Input
        return _deviceManager.FindVirtualCablePlayback();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _loopbackCapture.Dispose();
        _virtualMicRenderer.Dispose();
        _deviceManager.Dispose();
    }
}

/// <summary>
/// 音频桥接状态
/// </summary>
public record AudioBridgeStatus(
    bool IsRunning,
    bool IsLoopbackCapturing,
    bool IsVirtualMicRendering,
    int VirtualMicBufferedMs,
    long VirtualMicUnderrunCount,
    long VirtualMicFramesWritten
);
