using System.Buffers.Binary;
using System.Numerics;

namespace Shared.Models.Formats;

public static class BitstreamHelpers
{
  /// <summary>
  /// Eight bytes from a byte position, reading zeros past the end. Callers range-check what they
  /// actually consume; this only has to not fault.
  /// </summary>
  private static ulong Window(ReadOnlySpan<byte> data, int at)
  {
    if (at + sizeof(ulong) <= data.Length)
      return BinaryPrimitives.ReadUInt64BigEndian(data[at..]);

    ulong window = 0;
    for (var i = 0; i < sizeof(ulong); i++)
      window = (window << 8) | (at + i < data.Length ? data[at + i] : 0ul);

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
    if (count < 0 || count > 32)
      throw new ArgumentOutOfRangeException(nameof(count));
    if (bitOffset + count > (data.Length << 3))
      throw new InvalidDataException("Bitstream truncated");
    if (count == 0)
      return 0;

    return (uint)((Window(data, bitOffset >> 3) << (bitOffset & 7)) >> (64 - count));
  }

  public static bool ReadBit(ReadOnlySpan<byte> data, ref int bitOffset)
  {
    if (bitOffset >= (data.Length << 3))
      throw new InvalidDataException("Bitstream truncated");

    var bit = (data[bitOffset >> 3] >> (7 - (bitOffset & 7))) & 1;
    bitOffset++;
    return bit != 0;
  }

  /// <summary>
  /// Past the end of the data the window reads zeros, which look like an endless prefix, so the run
  /// is capped at what is really there and a run reaching that cap is a truncated element.
  /// </summary>
  public static uint ReadExpGolomb(ReadOnlySpan<byte> data, ref int bitOffset)
  {
    var bitLimit = data.Length << 3;
    var available = bitLimit - bitOffset;
    if (available <= 0)
      return 0;

    var window = Window(data, bitOffset >> 3) << (bitOffset & 7);
    var leadingZeros = window == 0 ? available : BitOperations.LeadingZeroCount(window);
    var zeros = Math.Min(leadingZeros, available);

    if (zeros >= 32)
      throw new InvalidDataException("ExpGolomb leading-zero run too large");

    // The bit that ends the run is consumed with it, unless the data ran out before one arrived.
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

  /// <summary>Zero, zero, three - the sequence an encoder inserts to keep a start code unique.</summary>
  private static ReadOnlySpan<byte> EmulationPrevention => [0x00, 0x00, 0x03];

  /// <summary>
  /// Strips emulation prevention bytes into <paramref name="destination"/>, which must be at least
  /// as long as the NAL, and returns how much of it was written. Callers decoding every keyframe
  /// keep one buffer rather than allocating a copy of each picture.
  /// </summary>
  public static int ExtractRbsp(ReadOnlySpan<byte> nalData, Span<byte> destination)
  {
    var written = 0;

    while (true)
    {
      var at = nalData.IndexOf(EmulationPrevention);
      if (at < 0) break;

      // The two zeros are kept and belong to the picture; only the byte guarding them is dropped.
      nalData[..(at + 2)].CopyTo(destination[written..]);
      written += at + 2;
      nalData = nalData[(at + 3)..];
    }

    nalData.CopyTo(destination[written..]);
    return written + nalData.Length;
  }
}
