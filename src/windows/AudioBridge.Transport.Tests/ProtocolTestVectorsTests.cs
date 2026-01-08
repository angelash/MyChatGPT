using System.Text.Json;
using AudioBridge.Transport.Protocol;

namespace AudioBridge.Transport.Tests;

public class ProtocolTestVectorsTests
{
    [Fact]
    public void ProtocolTestVectors_File_IsValid_And_Decodable()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "tests", "ProtocolTestVectors.json");
        Assert.True(File.Exists(path), $"missing: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("ABP/1.0", root.GetProperty("proto").GetString());
        Assert.Equal("hex", root.GetProperty("encoding").GetString());

        foreach (var v in root.GetProperty("vectors").EnumerateArray())
        {
            var name = v.GetProperty("name").GetString() ?? "(null)";
            var expectedStreamId = (byte)v.GetProperty("streamId").GetInt32();
            var expectedSeq = (uint)v.GetProperty("seq").GetInt64();
            var expectedTs = (uint)v.GetProperty("timestampSamples").GetInt64();
            var expectedPayloadHex = v.GetProperty("payloadHex").GetString() ?? "";
            var frameHex = v.GetProperty("frameHex").GetString() ?? "";

            var bytes = HexToBytes(frameHex);
            var ok = AbpBinaryFrame.TryDecode(bytes, out var frame, out var error);
            Assert.True(ok, $"{name}: {error}");
            Assert.NotNull(frame);

            Assert.Equal((AbpStreamId)expectedStreamId, frame!.StreamId);
            Assert.Equal(expectedSeq, frame.Seq);
            Assert.Equal(expectedTs, frame.TimestampSamples);
            Assert.Equal(expectedPayloadHex.ToUpperInvariant(), BytesToHex(frame.Payload));

            // Encode back should be byte-identical
            Assert.Equal(bytes, frame.Encode());
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "design.md")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found (missing design.md)");
    }

    private static byte[] HexToBytes(string hex)
    {
        hex = hex.Trim();
        if (hex.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (hex.Length % 2 != 0)
        {
            throw new FormatException($"hex length must be even: {hex.Length}");
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static string BytesToHex(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        return Convert.ToHexString(bytes);
    }
}

