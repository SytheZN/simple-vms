using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class RbspExtractorTests
{
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
  public void NoEmulationPrevention_PassesThrough()
  {
    byte[] input = [0x67, 0x42, 0x00, 0x2A, 0x96];
    var result = RbspExtractor.Extract(input);

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
  public void SingleEmulationPrevention_Removed()
  {
    byte[] input = [0xAA, 0x00, 0x00, 0x03, 0xBB];
    var result = RbspExtractor.Extract(input);

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
  public void MultipleEmulationPreventions_AllRemoved()
  {
    byte[] input = [0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x03, 0x02];
    var result = RbspExtractor.Extract(input);

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
  public void TrailingZeros_NotStripped()
  {
    byte[] input = [0xFF, 0x00, 0x00];
    var result = RbspExtractor.Extract(input);

    Assert.That(result, Is.EqualTo(input));
  }
}
