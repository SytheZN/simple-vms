namespace Format.Fmp4;

public static class MdatBuilder
{
  public static byte[] Build(IReadOnlyList<ReadOnlyMemory<byte>> annexBNals)
  {
    var payloadSize = 0;
    foreach (var nal in annexBNals)
      payloadSize += NalConverter.LengthPrefixedSize(nal.Span);

    var result = new byte[8 + payloadSize];
    var totalSize = (uint)result.Length;
    result[0] = (byte)(totalSize >> 24);
    result[1] = (byte)(totalSize >> 16);
    result[2] = (byte)(totalSize >> 8);
    result[3] = (byte)totalSize;
    result[4] = (byte)'m';
    result[5] = (byte)'d';
    result[6] = (byte)'a';
    result[7] = (byte)'t';

    var offset = 8;
    foreach (var nal in annexBNals)
    {
      var written = NalConverter.WriteLengthPrefixed(nal.Span, result.AsSpan(offset));
      offset += written;
    }

    return result;
  }
}
