using System.Buffers.Binary;

namespace AudioBridge.Transport.Audio;

/// <summary>
/// IMA ADPCM（4-bit）单声道 PCM16 编解码。
/// - 每个帧独立：payload 头部包含 predictor(int16) + index(byte) + reserved(byte)
/// - 紧随其后是 4-bit nibble 数据：每个样本（除第一个）占 4bit
/// - nibble 打包：先低 4bit、后高 4bit
/// </summary>
public static class ImaAdpcm
{
    // 标准 IMA ADPCM StepTable（89）
    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
        724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
        3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    // 标准 IndexTable（16）
    private static readonly int[] IndexTable =
    [
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8,
    ];

    public const int BlockHeaderSize = 4;

    public static byte[] EncodePcm16Mono(ReadOnlySpan<byte> pcmLittleEndian, int expectedSamples)
    {
        if (expectedSamples <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSamples), "expectedSamples must be > 1");
        }

        var expectedBytes = expectedSamples * 2;
        if (pcmLittleEndian.Length != expectedBytes)
        {
            throw new ArgumentException($"pcm length mismatch: {pcmLittleEndian.Length} != {expectedBytes}", nameof(pcmLittleEndian));
        }

        // payload: header(4) + ceil((samples-1)/2) bytes
        var nibbleCount = expectedSamples - 1;
        var dataBytes = (nibbleCount + 1) / 2;
        var outBuf = new byte[BlockHeaderSize + dataBytes];

        // predictor = first sample
        var predictor = BinaryPrimitives.ReadInt16LittleEndian(pcmLittleEndian.Slice(0, 2));

        // index：用第一个 delta 粗估一个起始 step（比固定 0 更稳）
        var index = EstimateStartIndex(pcmLittleEndian, predictor);

        BinaryPrimitives.WriteInt16LittleEndian(outBuf.AsSpan(0, 2), predictor);
        outBuf[2] = (byte)index;
        outBuf[3] = 0;

        var outPos = BlockHeaderSize;
        byte pack = 0;
        var packLow = true;

        var pred = predictor;
        var idx = index;

        for (var s = 1; s < expectedSamples; s++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(pcmLittleEndian.Slice(s * 2, 2));
            var nibble = EncodeNibble(sample, ref pred, ref idx);

            if (packLow)
            {
                pack = (byte)(nibble & 0x0F);
                packLow = false;
            }
            else
            {
                pack |= (byte)((nibble & 0x0F) << 4);
                outBuf[outPos++] = pack;
                pack = 0;
                packLow = true;
            }
        }

        if (!packLow)
        {
            // 还剩一个低 nibble
            outBuf[outPos] = pack;
        }

        return outBuf;
    }

    public static byte[] DecodeToPcm16Mono(ReadOnlySpan<byte> adpcmPayload, int expectedSamples)
    {
        if (expectedSamples <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSamples), "expectedSamples must be > 1");
        }

        if (adpcmPayload.Length < BlockHeaderSize)
        {
            throw new ArgumentException($"adpcm payload too short: {adpcmPayload.Length}", nameof(adpcmPayload));
        }

        var nibbleCount = expectedSamples - 1;
        var expectedDataBytes = (nibbleCount + 1) / 2;
        var expectedTotal = BlockHeaderSize + expectedDataBytes;
        if (adpcmPayload.Length != expectedTotal)
        {
            throw new ArgumentException($"adpcm payload length mismatch: {adpcmPayload.Length} != {expectedTotal}", nameof(adpcmPayload));
        }

        var predictor = BinaryPrimitives.ReadInt16LittleEndian(adpcmPayload.Slice(0, 2));
        var index = adpcmPayload[2];
        if (index > 88) index = 88;

        var pcm = new byte[expectedSamples * 2];
        BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(0, 2), predictor);

        var pred = predictor;
        var idx = (int)index;
        var inPos = BlockHeaderSize;
        var useLow = true;
        byte cur = adpcmPayload[inPos];

        for (var s = 1; s < expectedSamples; s++)
        {
            var nibble = useLow ? (cur & 0x0F) : ((cur >> 4) & 0x0F);

            // 用完高 nibble 后再移动到下一个字节（与打包顺序对齐）
            if (!useLow)
            {
                inPos++;
                if (inPos < adpcmPayload.Length)
                {
                    cur = adpcmPayload[inPos];
                }
            }
            useLow = !useLow;

            var decoded = DecodeNibble(nibble, ref pred, ref idx);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(s * 2, 2), decoded);
        }

        return pcm;
    }

    private static int EstimateStartIndex(ReadOnlySpan<byte> pcm, short predictor)
    {
        if (pcm.Length < 4)
        {
            return 0;
        }

        var next = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(2, 2));
        var diff = Math.Abs(next - predictor);
        // 找到最接近 diff 的 step
        var idx = 0;
        while (idx < StepTable.Length - 1 && StepTable[idx] < diff)
        {
            idx++;
        }
        return idx;
    }

    private static byte EncodeNibble(short sample, ref short predictor, ref int index)
    {
        var step = StepTable[index];

        var diff = sample - predictor;
        var sign = 0;
        if (diff < 0)
        {
            sign = 8;
            diff = -diff;
        }

        var delta = 0;
        var vpdiff = step >> 3;

        if (diff >= step)
        {
            delta |= 4;
            diff -= step;
            vpdiff += step;
        }
        if (diff >= (step >> 1))
        {
            delta |= 2;
            diff -= (step >> 1);
            vpdiff += (step >> 1);
        }
        if (diff >= (step >> 2))
        {
            delta |= 1;
            vpdiff += (step >> 2);
        }

        if (sign != 0)
        {
            predictor = (short)ClampToInt16(predictor - vpdiff);
        }
        else
        {
            predictor = (short)ClampToInt16(predictor + vpdiff);
        }

        index += IndexTable[delta];
        if (index < 0) index = 0;
        if (index > 88) index = 88;

        return (byte)(delta | sign);
    }

    private static short DecodeNibble(int nibble, ref short predictor, ref int index)
    {
        var step = StepTable[index];
        var sign = (nibble & 8) != 0;
        var delta = nibble & 7;

        var vpdiff = step >> 3;
        if ((delta & 4) != 0) vpdiff += step;
        if ((delta & 2) != 0) vpdiff += (step >> 1);
        if ((delta & 1) != 0) vpdiff += (step >> 2);

        if (sign)
        {
            predictor = (short)ClampToInt16(predictor - vpdiff);
        }
        else
        {
            predictor = (short)ClampToInt16(predictor + vpdiff);
        }

        index += IndexTable[delta];
        if (index < 0) index = 0;
        if (index > 88) index = 88;

        return predictor;
    }

    private static int ClampToInt16(int v)
    {
        if (v > short.MaxValue) return short.MaxValue;
        if (v < short.MinValue) return short.MinValue;
        return v;
    }
}

