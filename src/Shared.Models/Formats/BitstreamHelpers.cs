namespace Shared.Models.Formats;

public static class BitstreamHelpers
{
  public static uint ReadBits(ReadOnlySpan<byte> data, ref int bitOffset, int count)
  {
    if (count < 0 || count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));
    if (bitOffset + count > (data.Length << 3))
      throw new InvalidDataException("Bitstream truncated");
    uint result = 0;
    for (var i = 0; i < count; i++)
    {
      var byteIndex = bitOffset >> 3;
      var bitIndex = 7 - (bitOffset & 7);
      result = (result << 1) | (uint)((data[byteIndex] >> bitIndex) & 1);
      bitOffset++;
    }
    return result;
  }

  public static bool ReadBit(ReadOnlySpan<byte> data, ref int bitOffset) =>
    ReadBits(data, ref bitOffset, 1) != 0;

  public static uint ReadExpGolomb(ReadOnlySpan<byte> data, ref int bitOffset)
  {
    var bitLimit = data.Length << 3;
    var leadingZeros = 0;
    while (bitOffset < bitLimit && !ReadBit(data, ref bitOffset))
      leadingZeros++;
    if (leadingZeros >= 32)
      throw new InvalidDataException("ExpGolomb leading-zero run too large");
    if (bitOffset >= bitLimit && leadingZeros > 0)
      throw new InvalidDataException("ExpGolomb truncated: ran past end of data");
    if (leadingZeros == 0)
      return 0;
    return (1u << leadingZeros) - 1 + ReadBits(data, ref bitOffset, leadingZeros);
  }

  public static int ReadSignedExpGolomb(ReadOnlySpan<byte> data, ref int bitOffset)
  {
    var value = ReadExpGolomb(data, ref bitOffset);
    if (value == 0)
      return 0;
    var sign = (value & 1) == 1 ? 1 : -1;
    return sign * (int)((value + 1) >> 1);
  }

  public static void Skip(ref int bitOffset, int bits) => bitOffset += bits;

  public static byte[] ExtractRbsp(ReadOnlySpan<byte> nalData)
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

    Array.Resize(ref result, j);
    return result;
  }
}
