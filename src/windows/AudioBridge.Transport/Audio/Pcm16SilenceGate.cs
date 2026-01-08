namespace AudioBridge.Transport.Audio;

/// <summary>
/// PCM16 单声道静音门（用于省流：静音连续一段时间后停止发送）。
/// 使用 avg(|sample|) 做能量估计，配合连续帧计数实现简单的 hangover。
/// </summary>
public sealed class Pcm16SilenceGate
{
    private readonly int _thresholdAvgAbs;
    private readonly int _minSilentFramesToSuppress;
    private int _silentRun;
    private bool _suppressing;

    public Pcm16SilenceGate(int thresholdAvgAbs = 120, int minSilentFramesToSuppress = 10)
    {
        if (thresholdAvgAbs < 0) throw new ArgumentOutOfRangeException(nameof(thresholdAvgAbs));
        if (minSilentFramesToSuppress < 1) throw new ArgumentOutOfRangeException(nameof(minSilentFramesToSuppress));
        _thresholdAvgAbs = thresholdAvgAbs;
        _minSilentFramesToSuppress = minSilentFramesToSuppress;
    }

    public bool IsSuppressing => _suppressing;
    public int SilentRunFrames => _silentRun;

    public void Reset()
    {
        _silentRun = 0;
        _suppressing = false;
    }

    /// <summary>
    /// 是否应该发送该帧（true=发送；false=丢弃以省流）。
    /// </summary>
    public bool ShouldSend(ReadOnlySpan<byte> pcm16LittleEndian)
    {
        var avgAbs = AvgAbs(pcm16LittleEndian);
        var silent = avgAbs < _thresholdAvgAbs;

        if (silent)
        {
            _silentRun++;
            if (_silentRun >= _minSilentFramesToSuppress)
            {
                _suppressing = true;
            }
        }
        else
        {
            _silentRun = 0;
            _suppressing = false;
        }

        return !_suppressing;
    }

    private static int AvgAbs(ReadOnlySpan<byte> pcm16LittleEndian)
    {
        if (pcm16LittleEndian.Length < 2) return 0;
        if (pcm16LittleEndian.Length % 2 != 0) return 0;

        long sum = 0;
        var samples = pcm16LittleEndian.Length / 2;
        for (var i = 0; i < pcm16LittleEndian.Length; i += 2)
        {
            short s = (short)(pcm16LittleEndian[i] | (pcm16LittleEndian[i + 1] << 8));
            sum += Math.Abs((int)s);
        }

        return (int)(sum / samples);
    }
}

