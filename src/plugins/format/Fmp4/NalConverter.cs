using System.Buffers.Binary;

namespace Format.Fmp4;

public static class NalConverter
{
  public static ReadOnlyMemory<byte> AnnexBToLengthPrefixed(ReadOnlyMemory<byte> annexB)
  {
    var span = annexB.Span;
    var startCodeLen = DetectStartCodeLength(span);
    if (startCodeLen == 0)
      return annexB;

    var nalLen = span.Length - startCodeLen;
    var result = new byte[4 + nalLen];
    BinaryPrimitives.WriteUInt32BigEndian(result, (uint)nalLen);
    span[startCodeLen..].CopyTo(result.AsSpan(4));
    return result;
  }

  public static ReadOnlyMemory<byte> LengthPrefixedToAnnexB(ReadOnlyMemory<byte> lengthPrefixed)
  {
    if (lengthPrefixed.Length < 4)
      return lengthPrefixed;

    var nalLen = lengthPrefixed.Length - 4;
    var result = new byte[4 + nalLen];
    result[0] = 0;
    result[1] = 0;
    result[2] = 0;
    result[3] = 1;
    lengthPrefixed.Span[4..].CopyTo(result.AsSpan(4));
    return result;
  }

  public static ReadOnlySpan<byte> StripStartCode(ReadOnlySpan<byte> annexB)
  {
    var len = DetectStartCodeLength(annexB);
    return len > 0 ? annexB[len..] : annexB;
  }

  public static int WriteLengthPrefixed(ReadOnlySpan<byte> nal, Span<byte> dest)
  {
    var startCodeLen = DetectStartCodeLength(nal);
    var nalData = nal[startCodeLen..];
    BinaryPrimitives.WriteUInt32BigEndian(dest, (uint)nalData.Length);
    nalData.CopyTo(dest[4..]);
    return 4 + nalData.Length;
  }

  public static int LengthPrefixedSize(ReadOnlySpan<byte> nal)
  {
    var startCodeLen = DetectStartCodeLength(nal);
    return 4 + nal.Length - startCodeLen;
  }

  private static int DetectStartCodeLength(ReadOnlySpan<byte> data)
  {
    if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 1)
      return 4;
    if (data.Length >= 3 && data[0] == 0 && data[1] == 0 && data[2] == 1)
      return 3;
    return 0;
  }
}
