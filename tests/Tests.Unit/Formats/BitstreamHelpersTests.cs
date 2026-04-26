using Shared.Models.Formats;
using static Shared.Models.Formats.BitstreamHelpers;

namespace Tests.Unit.Formats;

[TestFixture]
public class BitstreamHelpersTests
{
  /// <summary>
  /// SCENARIO:
  /// Read individual bits from a known byte
  ///
  /// ACTION:
  /// Read 8 single bits from 0b10110010
  ///
  /// EXPECTED RESULT:
  /// Bits match the binary representation MSB-first
  /// </summary>
  [Test]
  public void ReadBit_ReturnsBitsMsbFirst()
  {
    ReadOnlySpan<byte> data = [0b10110010];
    bool[] expected = [true, false, true, true, false, false, true, false];
    var bitOffset = 0;

    for (var i = 0; i < 8; i++)
      Assert.That(ReadBit(data, ref bitOffset), Is.EqualTo(expected[i]), $"Bit {i}");
  }

  /// <summary>
  /// SCENARIO:
  /// Read multi-bit values from bytes
  ///
  /// ACTION:
  /// Read 3 bits, then 5 bits from 0b11010110
  ///
  /// EXPECTED RESULT:
  /// First read returns 6 (0b110), second returns 22 (0b10110)
  /// </summary>
  [Test]
  public void ReadBits_ReturnsCorrectMultiBitValues()
  {
    ReadOnlySpan<byte> data = [0b11010110];
    var bitOffset = 0;

    Assert.That(ReadBits(data, ref bitOffset, 3), Is.EqualTo(0b110u));
    Assert.That(ReadBits(data, ref bitOffset, 5), Is.EqualTo(0b10110u));
  }

  /// <summary>
  /// SCENARIO:
  /// Read unsigned Exp-Golomb coded values
  ///
  /// ACTION:
  /// Read known Exp-Golomb values: 0 (1), 1 (010), 2 (011), 3 (00100)
  ///
  /// EXPECTED RESULT:
  /// Decoded values match expected
  /// </summary>
  [Test]
  public void ReadExpGolomb_DecodesKnownValues()
  {
    ReadOnlySpan<byte> data = [0xA6, 0x42, 0x80];
    var bitOffset = 0;

    Assert.That(ReadExpGolomb(data, ref bitOffset), Is.EqualTo(0u));
    Assert.That(ReadExpGolomb(data, ref bitOffset), Is.EqualTo(1u));
    Assert.That(ReadExpGolomb(data, ref bitOffset), Is.EqualTo(2u));
    Assert.That(ReadExpGolomb(data, ref bitOffset), Is.EqualTo(3u));
    Assert.That(ReadExpGolomb(data, ref bitOffset), Is.EqualTo(4u));
  }

  /// <summary>
  /// SCENARIO:
  /// Read signed Exp-Golomb coded values
  ///
  /// ACTION:
  /// Read signed values: 0 (1), 1 (010), -1 (011), 2 (00100), -2 (00101)
  ///
  /// EXPECTED RESULT:
  /// Decoded signed values match expected
  /// </summary>
  [Test]
  public void ReadSignedExpGolomb_DecodesKnownValues()
  {
    ReadOnlySpan<byte> data = [0xA6, 0x42, 0x80];
    var bitOffset = 0;

    Assert.That(ReadSignedExpGolomb(data, ref bitOffset), Is.EqualTo(0));
    Assert.That(ReadSignedExpGolomb(data, ref bitOffset), Is.EqualTo(1));
    Assert.That(ReadSignedExpGolomb(data, ref bitOffset), Is.EqualTo(-1));
    Assert.That(ReadSignedExpGolomb(data, ref bitOffset), Is.EqualTo(2));
    Assert.That(ReadSignedExpGolomb(data, ref bitOffset), Is.EqualTo(-2));
  }

  /// <summary>
  /// SCENARIO:
  /// Skip bits and continue reading
  ///
  /// ACTION:
  /// Skip 4 bits, read 4 bits from 0xAB
  ///
  /// EXPECTED RESULT:
  /// Returns 0xB (lower nibble)
  /// </summary>
  [Test]
  public void Skip_AdvancesBitPosition()
  {
    ReadOnlySpan<byte> data = [0xAB];
    var bitOffset = 0;
    Skip(ref bitOffset, 4);

    Assert.That(ReadBits(data, ref bitOffset, 4), Is.EqualTo(0xBu));
  }

  /// <summary>
  /// SCENARIO:
  /// Data with no emulation prevention bytes
  ///
  /// ACTION:
  /// Extract RBSP from clean data
  ///
  /// EXPECTED RESULT:
  /// Output matches input exactly
  /// </summary>
  [Test]
  public void ExtractRbsp_NoEmulationPrevention_PassesThrough()
  {
    byte[] input = [0x67, 0x42, 0x00, 0x2A, 0x96];
    var result = ExtractRbsp(input);

    Assert.That(result, Is.EqualTo(input));
  }

  /// <summary>
  /// SCENARIO:
  /// Data with a single 00 00 03 emulation prevention sequence
  ///
  /// ACTION:
  /// Extract RBSP
  ///
  /// EXPECTED RESULT:
  /// 00 00 03 becomes 00 00 (03 removed)
  /// </summary>
  [Test]
  public void ExtractRbsp_SingleEmulationPrevention_Removed()
  {
    byte[] input = [0xAA, 0x00, 0x00, 0x03, 0xBB];
    var result = ExtractRbsp(input);

    Assert.That(result, Is.EqualTo(new byte[] { 0xAA, 0x00, 0x00, 0xBB }));
  }

  /// <summary>
  /// SCENARIO:
  /// Data with multiple emulation prevention sequences
  ///
  /// ACTION:
  /// Extract RBSP from data with two 00 00 03 patterns
  ///
  /// EXPECTED RESULT:
  /// Both 03 bytes removed
  /// </summary>
  [Test]
  public void ExtractRbsp_MultipleEmulationPreventions_AllRemoved()
  {
    byte[] input = [0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x03, 0x02];
    var result = ExtractRbsp(input);

    Assert.That(result, Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x00, 0x02 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Data ending with 00 00 (no third byte)
  ///
  /// ACTION:
  /// Extract RBSP
  ///
  /// EXPECTED RESULT:
  /// Trailing 00 00 preserved as-is
  /// </summary>
  [Test]
  public void ExtractRbsp_TrailingZeros_NotStripped()
  {
    byte[] input = [0xFF, 0x00, 0x00];
    var result = ExtractRbsp(input);

    Assert.That(result, Is.EqualTo(input));
  }
}
