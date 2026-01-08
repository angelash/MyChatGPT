namespace AudioBridge.Transport.Audio;

/// <summary>
/// ABP v1 固定音频格式常量（与两端约定一致）。
/// 注意：Transport 层不依赖 Core 项目，因此在此处重复定义。
/// </summary>
public static class AbpAudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;
    public const int FrameMs = 20;

    public const int SamplesPerFrame = SampleRate * FrameMs / 1000; // 960
    public const int BytesPerPcmFrame = SamplesPerFrame * 2; // PCM16 mono => 1920
}

