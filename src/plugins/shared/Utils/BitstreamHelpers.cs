using System.Buffers.Binary;
using System.Numerics;

namespace Utils;

public static class BitstreamHelpers
{
  private const int BitsPerByte = 8;
  private const int MaxReadBits = 32;
  private const int WindowBits = 64;
  private const int ExpGolombLeadingZeroLimit = 32;

  private static ulong Window(ReadOnlySpan<byte> data, int at)
  {
    if (at + sizeof(ulong) <= data.Length)
      return BinaryPrimitives.ReadUInt64BigEndian(data[at..]);

    ulong window = 0;
    for (var i = 0; i < sizeof(ulong); i++)
      window = (window << BitsPerByte) | (at + i < data.Length ? data[at + i] : 0ul);

    return window;
  }

  public static uint ReadBits(ReadOnlySpan<byte> data, ref int bitOffset, int count)
  {
    var value = PeekBits(data, bitOffset, count);
    bitOffset += count;
    return value;
  }

  public static uint PeekBits(ReadOnlySpan<byte> data, int bitOffset, int count)
  {
    if (count < 0 || count > MaxReadBits)
      throw new ArgumentOutOfRangeException(nameof(count));
    if (bitOffset + count > (data.Length * BitsPerByte))
      throw new InvalidDataException("Bitstream truncated");
    if (count == 0)
      return 0;

    return (uint)((Window(data, bitOffset / BitsPerByte) << (bitOffset % BitsPerByte)) >> (WindowBits - count));
  }

  public static bool ReadBit(ReadOnlySpan<byte> data, ref int bitOffset)
  {
    if (bitOffset >= (data.Length * BitsPerByte))
      throw new InvalidDataException("Bitstream truncated");

    var bit = (data[bitOffset / BitsPerByte] >> (BitsPerByte - 1 - (bitOffset % BitsPerByte))) & 1;
    bitOffset++;
    return bit != 0;
  }

  public static uint ReadExpGolomb(ReadOnlySpan<byte> data, ref int bitOffset)
  {
    var bitLimit = data.Length * BitsPerByte;
    var available = bitLimit - bitOffset;
    if (available <= 0)
      return 0;

    var window = Window(data, bitOffset / BitsPerByte) << (bitOffset % BitsPerByte);
    var leadingZeros = window == 0 ? available : BitOperations.LeadingZeroCount(window);
    var zeros = Math.Min(leadingZeros, available);

    if (zeros >= ExpGolombLeadingZeroLimit)
      throw new InvalidDataException("ExpGolomb leading-zero run too large");

    bitOffset += zeros < available ? zeros + 1 : zeros;

    if (bitOffset >= bitLimit && zeros > 0)
      throw new InvalidDataException("ExpGolomb truncated: ran past end of data");

    if (zeros == 0)
      return 0;

    return (1u << zeros) - 1 + ReadBits(data, ref bitOffset, zeros);
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
    Array.Resize(ref result, ExtractRbsp(nalData, result));
    return result;
  }

  private static ReadOnlySpan<byte> EmulationPrevention => [0x00, 0x00, 0x03];

  private const int EmulationSequenceKeptPrefix = 2;
  private const int EmulationSequenceLength = 3;

  public static int ExtractRbsp(ReadOnlySpan<byte> nalData, Span<byte> destination)
  {
    var written = 0;

    while (true)
    {
      var at = nalData.IndexOf(EmulationPrevention);
      if (at < 0) break;

      nalData[..(at + EmulationSequenceKeptPrefix)].CopyTo(destination[written..]);
      written += at + EmulationSequenceKeptPrefix;
      nalData = nalData[(at + EmulationSequenceLength)..];
    }

    nalData.CopyTo(destination[written..]);
    return written + nalData.Length;
  }
}
