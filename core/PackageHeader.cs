using System.Buffers.Binary;

namespace Ferry;

/// <summary>Container identification only; does not verify signatures or firmware compatibility.</summary>
public static class PackageHeader
{
    public const int ProbeSize = 0x440;
    public static string Validate(ReadOnlySpan<byte> header, long length)
    {
        if (length < ProbeSize || header.Length < ProbeSize) throw new LocalizedException(new("core.pkg.header"));
        var magic = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (magic == 0x7f464948) return "FIH";
        if (magic != 0x7f434e54 || BinaryPrimitives.ReadUInt64BigEndian(header[0x430..]) != (ulong)length)
            throw new LocalizedException(new("core.pkg.header"));
        return "CNT";
    }
}
