namespace AudioBridge.Core.Audio;

/// <summary>
/// 音频格式常量（ABP 协议固定格式）
/// </summary>
public static class AudioFormat
{
    /// <summary>采样率</summary>
    public const int SampleRate = 48000;

    /// <summary>通道数</summary>
    public const int Channels = 1;

    /// <summary>位深</summary>
    public const int BitsPerSample = 16;

    /// <summary>帧时长（毫秒）</summary>
    public const int FrameMs = 20;

    /// <summary>每帧采样数</summary>
    public const int SamplesPerFrame = SampleRate * FrameMs / 1000; // 960

    /// <summary>每帧字节数（PCM16 mono）</summary>
    public const int BytesPerFrame = SamplesPerFrame * 2; // 1920
}
