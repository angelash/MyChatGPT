using System.Buffers.Binary;

namespace AudioBridge.Transport.Protocol;

public sealed record AbpBinaryFrame(
    AbpStreamId StreamId,
    uint Seq,
    uint TimestampSamples,
    byte[] Payload)
{
    public byte[] Encode()
    {
        if (Payload.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Payload),
                $"payload too large: {Payload.Length} > {ushort.MaxValue}");
        }

        var payloadLen = (ushort)Payload.Length;
        var buffer = new byte[AbpConstants.HeaderSize + payloadLen];

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), AbpConstants.Magic);
        buffer[2] = AbpConstants.Version;
        buffer[3] = (byte)StreamId;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), Seq);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), TimestampSamples);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12, 2), payloadLen);

        Payload.AsSpan().CopyTo(buffer.AsSpan(AbpConstants.HeaderSize));
        return buffer;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> data,
        out AbpBinaryFrame? frame,
        out string? error)
    {
        frame = null;
        error = null;

        if (data.Length < AbpConstants.HeaderSize)
        {
            error = $"frame too short: {data.Length} < {AbpConstants.HeaderSize}";
            return false;
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0, 2));
        if (magic != AbpConstants.Magic)
        {
            error = $"bad magic: 0x{magic:X4}";
            return false;
        }

        var version = data[2];
        if (version != AbpConstants.Version)
        {
            error = $"unsupported version: {version}";
            return false;
        }

        var streamIdRaw = data[3];
        if (!Enum.IsDefined(typeof(AbpStreamId), streamIdRaw))
        {
            error = $"invalid streamId: {streamIdRaw}";
            return false;
        }

        var streamId = (AbpStreamId)streamIdRaw;
        var seq = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        var ts = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
        var payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2));

        var expectedLen = AbpConstants.HeaderSize + payloadLen;
        if (data.Length != expectedLen)
        {
            error = $"length mismatch: data={data.Length}, expected={expectedLen}";
            return false;
        }

        var payload = data.Slice(AbpConstants.HeaderSize, payloadLen).ToArray();
        frame = new AbpBinaryFrame(streamId, seq, ts, payload);
        return true;
    }
}

