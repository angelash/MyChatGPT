using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioBridge.Core.Audio;

/// <summary>
/// 虚拟麦克风渲染器：把 PCM 音频写入虚拟声卡的 Playback 端
/// </summary>
public class VirtualMicRenderer : IDisposable
{
    private WasapiOut? _waveOut;
    private BufferedWaveProvider? _buffer;
    private readonly object _lock = new();
    private bool _disposed;
    private bool _isRendering;

    // 统计
    private long _underrunCount;
    private long _framesWritten;

    /// <summary>当前是否正在渲染</summary>
    public bool IsRendering => _isRendering;

    /// <summary>缓冲区当前字节数</summary>
    public int BufferedBytes => _buffer?.BufferedBytes ?? 0;

    /// <summary>缓冲区当前时长（毫秒）</summary>
    public int BufferedMs => _buffer != null
        ? (int)(_buffer.BufferedBytes * 1000L / _buffer.WaveFormat.AverageBytesPerSecond)
        : 0;

    /// <summary>欠载次数（播放时缓冲区空了）</summary>
    public long UnderrunCount => _underrunCount;

    /// <summary>已写入帧数</summary>
    public long FramesWritten => _framesWritten;

    /// <summary>
    /// 启动渲染到指定设备
    /// </summary>
    public void Start(MMDevice device)
    {
        lock (_lock)
        {
            if (_isRendering) return;

            try
            {
                // 创建 48kHz mono PCM16 的缓冲
                var format = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels);

                _buffer = new BufferedWaveProvider(format)
                {
                    // 缓冲 200ms（避免抖动导致断断续续）
                    BufferLength = format.AverageBytesPerSecond / 5,
                    DiscardOnBufferOverflow = true // 宁可丢也别卡死
                };

                // 使用 WASAPI 独占模式写入（延迟更低）
                // 如果设备不支持，会自动降级到共享模式
                _waveOut = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Init(_buffer);
                _waveOut.Play();

                _isRendering = true;
                _underrunCount = 0;
                _framesWritten = 0;
            }
            catch (Exception)
            {
                Cleanup();
                throw;
            }
        }
    }

    /// <summary>
    /// 写入一帧 PCM 数据（20ms = 1920 字节）
    /// </summary>
    public void WriteFrame(byte[] pcmFrame)
    {
        if (!_isRendering || _buffer == null) return;

        lock (_lock)
        {
            if (_buffer == null) return;

            // 检查是否快欠载了
            if (_buffer.BufferedBytes < AudioFormat.BytesPerFrame)
            {
                Interlocked.Increment(ref _underrunCount);
            }

            _buffer.AddSamples(pcmFrame, 0, pcmFrame.Length);
            Interlocked.Increment(ref _framesWritten);
        }
    }

    /// <summary>
    /// 写入 PCM 数据（short[] 格式）
    /// </summary>
    public void WriteFrame(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        WriteFrame(bytes);
    }

    /// <summary>
    /// 停止渲染
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRendering) return;
            _waveOut?.Stop();
            _isRendering = false;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 可以在这里处理播放停止事件
        _isRendering = false;
    }

    private void Cleanup()
    {
        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }
        _buffer = null;
        _isRendering = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        Cleanup();
    }
}
