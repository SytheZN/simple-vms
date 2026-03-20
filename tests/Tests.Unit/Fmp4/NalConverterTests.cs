using Format.Fmp4;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class NalConverterTests
{
  /// <summary>
  /// SCENARIO:
  /// Convert Annex B NAL (4-byte start code) to length-prefixed
  ///
  /// ACTION:
  /// AnnexBToLengthPrefixed with 00 00 00 01 + 3 bytes of NAL data
  ///
  /// EXPECTED RESULT:
  /// 4-byte big-endian length (3) followed by the NAL data
  /// </summary>
  [Test]
  public void AnnexBToLengthPrefixed_FourByteStartCode()
  {
    byte[] annexB = [0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x04];
    var result = NalConverter.AnnexBToLengthPrefixed(annexB);

    Assert.That(result.Length, Is.EqualTo(7));
    Assert.That(result.Span[0..4].ToArray(), Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x03 }));
    Assert.That(result.Span[4..].ToArray(), Is.EqualTo(new byte[] { 0x65, 0x88, 0x04 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Convert Annex B NAL (3-byte start code) to length-prefixed
  ///
  /// ACTION:
  /// AnnexBToLengthPrefixed with 00 00 01 + 2 bytes of NAL data
  ///
  /// EXPECTED RESULT:
  /// 4-byte big-endian length (2) followed by the NAL data
  /// </summary>
  [Test]
  public void AnnexBToLengthPrefixed_ThreeByteStartCode()
  {
    byte[] annexB = [0x00, 0x00, 0x01, 0x67, 0x42];
    var result = NalConverter.AnnexBToLengthPrefixed(annexB);

    Assert.That(result.Length, Is.EqualTo(6));
    Assert.That(result.Span[0..4].ToArray(), Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x02 }));
    Assert.That(result.Span[4..].ToArray(), Is.EqualTo(new byte[] { 0x67, 0x42 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Round-trip: Annex B -> length-prefixed -> Annex B
  ///
  /// ACTION:
  /// Convert to length-prefixed, then back to Annex B
  ///
  /// EXPECTED RESULT:
  /// Result matches original (with 4-byte start code)
  /// </summary>
  [Test]
  public void RoundTrip_PreservesNalData()
  {
    byte[] original = [0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x04, 0xFF];
    var lengthPrefixed = NalConverter.AnnexBToLengthPrefixed(original);
    var roundTripped = NalConverter.LengthPrefixedToAnnexB(lengthPrefixed);

    Assert.That(roundTripped.Span.ToArray(), Is.EqualTo(original));
  }

  /// <summary>
  /// SCENARIO:
  /// Strip start code from Annex B data
  ///
  /// ACTION:
  /// StripStartCode on data with 4-byte start code
  ///
  /// EXPECTED RESULT:
  /// Returns only the NAL bytes without the start code
  /// </summary>
  [Test]
  public void StripStartCode_RemovesFourBytePrefix()
  {
    byte[] annexB = [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00];
    var stripped = NalConverter.StripStartCode(annexB);

    Assert.That(stripped.ToArray(), Is.EqualTo(new byte[] { 0x67, 0x42, 0x00 }));
  }

  /// <summary>
  /// SCENARIO:
  /// LengthPrefixedSize calculates correct output size
  ///
  /// ACTION:
  /// Call LengthPrefixedSize on Annex B data
  ///
  /// EXPECTED RESULT:
  /// Returns 4 (length prefix) + NAL data length (without start code)
  /// </summary>
  [Test]
  public void LengthPrefixedSize_CalculatesCorrectly()
  {
    byte[] annexB = [0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x04];
    var size = NalConverter.LengthPrefixedSize(annexB);

    Assert.That(size, Is.EqualTo(7));
  }
}
