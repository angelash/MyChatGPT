namespace AudioBridge.Transport.Protocol;

/// <summary>
/// Minimal sequence tracker for packet loss/duplication estimation.
/// Assumes sender uses monotonically increasing uint32 sequence.
/// </summary>
public sealed class SeqTracker
{
    private uint? _lastSeq;

    public ulong Received { get; private set; }
    public ulong Lost { get; private set; }
    public ulong DuplicatedOrOutOfOrder { get; private set; }

    public uint? LastSeq => _lastSeq;

    public void Reset()
    {
        _lastSeq = null;
        Received = 0;
        Lost = 0;
        DuplicatedOrOutOfOrder = 0;
    }

    public void OnPacket(uint seq)
    {
        Received++;

        if (_lastSeq is null)
        {
            _lastSeq = seq;
            return;
        }

        var last = _lastSeq.Value;
        if (seq == last)
        {
            DuplicatedOrOutOfOrder++;
            return;
        }

        if (seq < last)
        {
            DuplicatedOrOutOfOrder++;
            return;
        }

        var gap = seq - last;
        if (gap > 1)
        {
            Lost += gap - 1;
        }

        _lastSeq = seq;
    }
}

