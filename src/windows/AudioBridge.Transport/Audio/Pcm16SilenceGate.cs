namespace AudioBridge.Transport.Audio;

/// <summary>
/// PCM16 单声道静音门（用于省流：静音连续一段时间后停止发送）。
/// 使用 avg(|sample|) 做能量估计，配合连续帧计数实现简单的 hangover。
/// 支持动态修改阈值参数。
/// </summary>
public sealed class Pcm16SilenceGate
{
    private int _thresholdAvgAbs;
    private int _minSilentFramesToSuppress;
    private int _silentRun;
    private bool _suppressing;
    private int _lastAvgAbs; // 最后一次计算的平均绝对值（用于调试/监控）

    public Pcm16SilenceGate(int thresholdAvgAbs = 120, int minSilentFramesToSuppress = 10)
    {
        if (thresholdAvgAbs < 0) throw new ArgumentOutOfRangeException(nameof(thresholdAvgAbs));
        if (minSilentFramesToSuppress < 1) throw new ArgumentOutOfRangeException(nameof(minSilentFramesToSuppress));
        _thresholdAvgAbs = thresholdAvgAbs;
        _minSilentFramesToSuppress = minSilentFramesToSuppress;
    }

    /// <summary>当前静音门阈值（可动态修改）</summary>
    public int ThresholdAvgAbs
    {
        get => _thresholdAvgAbs;
        set => _thresholdAvgAbs = Math.Clamp(value, 0, 32767);
    }

    /// <summary>最小静音帧数（可动态修改）</summary>
    public int MinSilentFramesToSuppress
    {
        get => _minSilentFramesToSuppress;
        set => _minSilentFramesToSuppress = Math.Clamp(value, 1, 100);
    }

    public bool IsSuppressing => _suppressing;
    public int SilentRunFrames => _silentRun;

    /// <summary>最后一帧的平均绝对值（用于监控/调试）</summary>
    public int LastAvgAbs => _lastAvgAbs;

    public void Reset()
    {
        _silentRun = 0;
        _suppressing = false;
        _lastAvgAbs = 0;
    }

    /// <summary>
    /// 是否应该发送该帧（true=发送；false=丢弃以省流）。
    /// </summary>
    public bool ShouldSend(ReadOnlySpan<byte> pcm16LittleEndian)
    {
        var avgAbs = AvgAbs(pcm16LittleEndian);
        _lastAvgAbs = avgAbs;
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

