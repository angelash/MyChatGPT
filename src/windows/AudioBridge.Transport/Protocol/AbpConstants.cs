namespace AudioBridge.Transport.Protocol;

public static class AbpConstants
{
    public const string Proto = "ABP/1.0";

    public const ushort Magic = 0xAB01;
    public const byte Version = 1;

    /// <summary>
    /// Binary header size in bytes.
    /// Layout:
    /// 0-1 magic(u16), 2 version(u8), 3 streamId(u8),
    /// 4-7 seq(u32), 8-11 timestampSamples(u32), 12-13 payloadLen(u16)
    /// </summary>
    public const int HeaderSize = 14;
}

