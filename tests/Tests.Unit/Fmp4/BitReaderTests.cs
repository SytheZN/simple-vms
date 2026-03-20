using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class BitReaderTests
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
    var reader = new BitReader([0b10110010]);
    bool[] expected = [true, false, true, true, false, false, true, false];

    for (var i = 0; i < 8; i++)
      Assert.That(reader.ReadBit(), Is.EqualTo(expected[i]), $"Bit {i}");
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
    var reader = new BitReader([0b11010110]);

    Assert.That(reader.ReadBits(3), Is.EqualTo(0b110u));
    Assert.That(reader.ReadBits(5), Is.EqualTo(0b10110u));
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
    // 0 = 1b, 1 = 010b, 2 = 011b, 3 = 00100b, 4 = 00101b
    // Packed: 1_010_011_00100_00101 = 1010 0110 0100 0010 1(000)
    var reader = new BitReader([0xA6, 0x42, 0x80]);

    Assert.That(reader.ReadExpGolomb(), Is.EqualTo(0u));
    Assert.That(reader.ReadExpGolomb(), Is.EqualTo(1u));
    Assert.That(reader.ReadExpGolomb(), Is.EqualTo(2u));
    Assert.That(reader.ReadExpGolomb(), Is.EqualTo(3u));
    Assert.That(reader.ReadExpGolomb(), Is.EqualTo(4u));
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
    // Same bit pattern as unsigned: 0, 1, 2, 3, 4
    // Signed mapping: 0->0, 1->1, 2->-1, 3->2, 4->-2
    var reader = new BitReader([0xA6, 0x42, 0x80]);

    Assert.That(reader.ReadSignedExpGolomb(), Is.EqualTo(0));
    Assert.That(reader.ReadSignedExpGolomb(), Is.EqualTo(1));
    Assert.That(reader.ReadSignedExpGolomb(), Is.EqualTo(-1));
    Assert.That(reader.ReadSignedExpGolomb(), Is.EqualTo(2));
    Assert.That(reader.ReadSignedExpGolomb(), Is.EqualTo(-2));
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
    var reader = new BitReader([0xAB]);
    reader.Skip(4);

    Assert.That(reader.ReadBits(4), Is.EqualTo(0xBu));
  }
}
