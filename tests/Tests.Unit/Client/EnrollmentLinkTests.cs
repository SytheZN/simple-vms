using Client.Core;

namespace Tests.Unit.Client;

[TestFixture]
public class EnrollmentLinkTests
{
  /// <summary>
  /// SCENARIO:
  /// A well-formed svms://enroll link is parsed
  ///
  /// ACTION:
  /// TryParse "svms://enroll?address=192.168.1.50:8080&token=ABCD-EFGH"
  ///
  /// EXPECTED RESULT:
  /// Returns true with the address and token extracted
  /// </summary>
  [Test]
  public void TryParse_ValidLink_ReturnsAddressAndToken()
  {
    var ok = EnrollmentLink.TryParse("svms://enroll?address=192.168.1.50:8080&token=ABCD-EFGH", out var address, out var token);

    Assert.That(ok, Is.True);
    Assert.That(address, Is.EqualTo("192.168.1.50:8080"));
    Assert.That(token, Is.EqualTo("ABCD-EFGH"));
  }

  /// <summary>
  /// SCENARIO:
  /// The address is a bracketed IPv6 literal, URL-encoded by the web UI
  ///
  /// ACTION:
  /// TryParse a link whose address parameter is "%5Bfd00%3A%3A1%5D%3A8080"
  ///
  /// EXPECTED RESULT:
  /// Address is decoded to "[fd00::1]:8080"
  /// </summary>
  [Test]
  public void TryParse_EncodedIpv6Address_Decodes()
  {
    var ok = EnrollmentLink.TryParse("svms://enroll?address=%5Bfd00%3A%3A1%5D%3A8080&token=ABCD-EFGH", out var address, out _);

    Assert.That(ok, Is.True);
    Assert.That(address, Is.EqualTo("[fd00::1]:8080"));
  }

  /// <summary>
  /// SCENARIO:
  /// Links with the wrong scheme, wrong host, or missing parameters are offered
  ///
  /// ACTION:
  /// TryParse each malformed input
  ///
  /// EXPECTED RESULT:
  /// Returns false with null outputs for every case
  /// </summary>
  [TestCase("https://enroll?address=a:1&token=ABCD-EFGH")]
  [TestCase("svms://other?address=a:1&token=ABCD-EFGH")]
  [TestCase("svms://enroll?token=ABCD-EFGH")]
  [TestCase("svms://enroll?address=a:1")]
  [TestCase("svms://enroll?address=&token=ABCD-EFGH")]
  [TestCase("not a link")]
  [TestCase("")]
  [TestCase(null)]
  public void TryParse_Malformed_ReturnsFalse(string? link)
  {
    var ok = EnrollmentLink.TryParse(link, out var address, out var token);

    Assert.That(ok, Is.False);
    Assert.That(address, Is.Null);
    Assert.That(token, Is.Null);
  }
}
