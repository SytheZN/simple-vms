namespace Format.Fmp4;

public static class RbspExtractor
{
  public static byte[] Extract(ReadOnlySpan<byte> nalData)
  {
    var result = new byte[nalData.Length];
    var j = 0;
    var i = 0;

    while (i < nalData.Length)
    {
      if (i + 2 < nalData.Length
        && nalData[i] == 0x00
        && nalData[i + 1] == 0x00
        && nalData[i + 2] == 0x03)
      {
        result[j++] = 0x00;
        result[j++] = 0x00;
        i += 3;
      }
      else
      {
        result[j++] = nalData[i++];
      }
    }

    return result.AsSpan(0, j).ToArray();
  }
}
