using AudioBridge.Transport.Audio;

namespace AudioBridge.Transport.Tests;

public class ImaAdpcmTests
{
    [Fact]
    public void Encode_OutputLength_IsExpected()
    {
        var pcm = new byte[AbpAudioFormat.BytesPerPcmFrame]; // silence
        var adpcm = ImaAdpcm.EncodePcm16Mono(pcm, AbpAudioFormat.SamplesPerFrame);

        var nibbleCount = AbpAudioFormat.SamplesPerFrame - 1;
        var expectedDataBytes = (nibbleCount + 1) / 2;
        var expectedLen = ImaAdpcm.BlockHeaderSize + expectedDataBytes;

        Assert.Equal(expectedLen, adpcm.Length);
    }

    [Fact]
    public void EncodeDecode_Silence_RoundtripProducesSilence()
    {
        var pcm = new byte[AbpAudioFormat.BytesPerPcmFrame]; // all zeros
        var adpcm = ImaAdpcm.EncodePcm16Mono(pcm, AbpAudioFormat.SamplesPerFrame);
        var decoded = ImaAdpcm.DecodeToPcm16Mono(adpcm, AbpAudioFormat.SamplesPerFrame);

        Assert.Equal(pcm.Length, decoded.Length);
        Assert.True(decoded.All(b => b == 0), "decoded should be all zeros for silence input");
    }

    [Fact]
    public void Decode_WrongLength_Throws()
    {
        var bad = new byte[ImaAdpcm.BlockHeaderSize + 1];
        Assert.Throws<ArgumentException>(() => ImaAdpcm.DecodeToPcm16Mono(bad, AbpAudioFormat.SamplesPerFrame));
    }
}

