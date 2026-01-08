using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioBridge.Core.Audio;

/// <summary>
/// WASAPI Loopback 捕获：抓取系统播放声音
/// </summary>
public class LoopbackCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly object _lock = new();
    private bool _disposed;
    private bool _isCapturing;

    // 重采样与格式转换
    private WaveFormat? _sourceFormat;
    private BufferedWaveProvider? _buffer;
    private MediaFoundationResampler? _resampler;

    // PCM 帧缓冲（20ms = 960 samples = 1920 bytes）
    private readonly byte[] _frameBuffer = new byte[AudioFormat.BytesPerFrame];
    private int _frameBufferPos;

    /// <summary>
    /// 当收到完整的 20ms PCM 帧时触发
    /// </summary>
    public event Action<byte[]>? FrameAvailable;

    /// <summary>
    /// 当捕获出错时触发
    /// </summary>
    public event Action<Exception>? Error;

    /// <summary>
    /// 当前是否正在捕获
    /// </summary>
    public bool IsCapturing => _isCapturing;

    /// <summary>
    /// 使用指定设备开始捕获（null = 默认播放设备）
    /// </summary>
    public void Start(MMDevice? device = null)
    {
        lock (_lock)
        {
            if (_isCapturing) return;

            try
            {
                _capture = device != null
                    ? new WasapiLoopbackCapture(device)
                    : new WasapiLoopbackCapture();

                _sourceFormat = _capture.WaveFormat;

                // 目标格式：48kHz mono PCM16
                var targetFormat = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels);

                // 如果源格式与目标不同，需要重采样
                if (_sourceFormat.SampleRate != targetFormat.SampleRate ||
                    _sourceFormat.Channels != targetFormat.Channels ||
                    _sourceFormat.BitsPerSample != targetFormat.BitsPerSample)
                {
                    // 使用 BufferedWaveProvider 作为中间缓冲
                    _buffer = new BufferedWaveProvider(_sourceFormat)
                    {
                        BufferLength = _sourceFormat.AverageBytesPerSecond,
                        DiscardOnBufferOverflow = true
                    };
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isCapturing = true;
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                Cleanup();
            }
        }
    }

    /// <summary>
    /// 停止捕获
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isCapturing) return;
            _capture?.StopRecording();
            _isCapturing = false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        try
        {
            // 转换：float stereo → int16 mono
            var converted = ConvertToMono16(e.Buffer, e.BytesRecorded, _sourceFormat!);

            // 切帧：每 20ms（1920 字节）触发一次
            int offset = 0;
            while (offset < converted.Length)
            {
                int toCopy = Math.Min(converted.Length - offset, AudioFormat.BytesPerFrame - _frameBufferPos);
                Array.Copy(converted, offset, _frameBuffer, _frameBufferPos, toCopy);
                _frameBufferPos += toCopy;
                offset += toCopy;

                if (_frameBufferPos >= AudioFormat.BytesPerFrame)
                {
                    var frame = new byte[AudioFormat.BytesPerFrame];
                    Array.Copy(_frameBuffer, frame, AudioFormat.BytesPerFrame);
                    _frameBufferPos = 0;
                    FrameAvailable?.Invoke(frame);
                }
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Error?.Invoke(e.Exception);
        }
        Cleanup();
    }

    /// <summary>
    /// 将 WASAPI 输出（通常 float stereo）转换为 PCM16 mono
    /// </summary>
    private byte[] ConvertToMono16(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        // WASAPI Loopback 通常输出 IEEE float stereo
        int sourceSamples = bytesRecorded / (sourceFormat.BitsPerSample / 8) / sourceFormat.Channels;

        // 计算目标采样数（考虑采样率转换）
        int targetSamples = (int)((long)sourceSamples * AudioFormat.SampleRate / sourceFormat.SampleRate);
        var result = new byte[targetSamples * 2]; // 16-bit mono

        // 简化处理：先假设源是 32-bit float stereo 48kHz（最常见情况）
        if (sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat &&
            sourceFormat.BitsPerSample == 32 &&
            sourceFormat.SampleRate == AudioFormat.SampleRate)
        {
            int resultPos = 0;
            for (int i = 0; i < bytesRecorded && resultPos < result.Length - 1; i += sourceFormat.BlockAlign)
            {
                // 读取左右声道
                float left = BitConverter.ToSingle(buffer, i);
                float right = sourceFormat.Channels > 1
                    ? BitConverter.ToSingle(buffer, i + 4)
                    : left;

                // 混合为 mono
                float mono = (left + right) / 2f;

                // 转换为 int16
                short sample = (short)Math.Clamp(mono * 32767f, short.MinValue, short.MaxValue);

                // 写入结果
                result[resultPos++] = (byte)(sample & 0xFF);
                result[resultPos++] = (byte)((sample >> 8) & 0xFF);
            }

            return result[..resultPos];
        }

        // 其他格式：使用 NAudio 的重采样器（更复杂，后续再优化）
        // 目前先返回静音
        return result;
    }

    private void Cleanup()
    {
        _resampler?.Dispose();
        _resampler = null;
        _buffer = null;

        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _frameBufferPos = 0;
        _isCapturing = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        Cleanup();
    }
}
