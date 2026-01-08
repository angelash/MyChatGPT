using AudioBridge.Transport.Protocol;

namespace AudioBridge.Transport.Tests;

public class AbpProtocolTests
{
    [Fact]
    public void BinaryFrame_Encode_ProducesExpectedBytes()
    {
        var frame = new AbpBinaryFrame(
            StreamId: AbpStreamId.Downlink,
            Seq: 1,
            TimestampSamples: 960,
            Payload: new byte[] { 0x01, 0x02, 0x03 });

        var bytes = frame.Encode();

        var expected = new byte[]
        {
            0x01, 0xAB, // magic = 0xAB01 (LE)
            0x01,       // version
            0x01,       // streamId = 1
            0x01, 0x00, 0x00, 0x00, // seq = 1 (LE)
            0xC0, 0x03, 0x00, 0x00, // ts = 960 (LE)
            0x03, 0x00, // payloadLen = 3 (LE)
            0x01, 0x02, 0x03, // payload
        };

        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void BinaryFrame_TryDecode_RoundTrip()
    {
        var original = new AbpBinaryFrame(
            StreamId: AbpStreamId.Uplink,
            Seq: 42,
            TimestampSamples: 123456,
            Payload: new byte[] { 0x10, 0x20 });

        var bytes = original.Encode();

        var ok = AbpBinaryFrame.TryDecode(bytes, out var decoded, out var error);

        Assert.True(ok, error);
        Assert.NotNull(decoded);
        Assert.Equal(original.StreamId, decoded!.StreamId);
        Assert.Equal(original.Seq, decoded.Seq);
        Assert.Equal(original.TimestampSamples, decoded.TimestampSamples);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact]
    public void BinaryFrame_TryDecode_RejectsBadMagic()
    {
        var bytes = new byte[AbpConstants.HeaderSize];
        bytes[0] = 0x00;
        bytes[1] = 0x00; // wrong magic
        bytes[2] = AbpConstants.Version;
        bytes[3] = (byte)AbpStreamId.Downlink;

        var ok = AbpBinaryFrame.TryDecode(bytes, out var decoded, out var error);

        Assert.False(ok);
        Assert.Null(decoded);
        Assert.Contains("magic", error);
    }

    [Fact]
    public void SeqTracker_CountsLoss()
    {
        var t = new SeqTracker();
        t.OnPacket(1);
        t.OnPacket(2);
        t.OnPacket(5);

        Assert.Equal((ulong)3, t.Received);
        Assert.Equal((ulong)2, t.Lost); // missing 3,4
    }
}