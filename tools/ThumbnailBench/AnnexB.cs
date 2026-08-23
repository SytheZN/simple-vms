namespace ThumbnailBench;

internal static class AnnexB
{
  public static List<byte[]> Split(byte[] stream)
  {
    var units = new List<byte[]>();
    var start = -1;

    for (var i = 0; i + 2 < stream.Length; i++)
    {
      if (stream[i] != 0 || stream[i + 1] != 0 || stream[i + 2] != 1) continue;

      if (start >= 0) units.Add(Slice(stream, start, TrimTrailingZeros(stream, start, i)));
      i += 2;
      start = i + 1;
    }

    if (start >= 0 && start < stream.Length)
      units.Add(Slice(stream, start, stream.Length));

    return units;
  }

  private static int TrimTrailingZeros(byte[] stream, int start, int end)
  {
    while (end > start && stream[end - 1] == 0) end--;
    return end;
  }

  private static byte[] Slice(byte[] stream, int start, int end) =>
    stream[start..end];

  public static byte Type(byte[] unit, Codec codec) => codec == Codec.H264
    ? (byte)(unit[0] & 0x1F)
    : (byte)((unit[0] >> 1) & 0x3F);

  public static byte RefIdc(byte[] unit) => (byte)((unit[0] >> 5) & 3);
}
