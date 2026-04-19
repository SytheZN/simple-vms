using Server.Core.PortForwarding;

namespace Tests.Unit.Core;

[TestFixture]
public class PortForwardingServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// The persisted protocol key was written during a prior successful apply
  /// ("nat-pmp" or "upnp"), is missing, or is a stray unrecognised value
  ///
  /// ACTION:
  /// PortForwardingService.ParseStoredProtocol(stored)
  ///
  /// EXPECTED RESULT:
  /// Valid values round-trip to the matching enum; missing or unknown values
  /// yield null so the service probes both protocols again
  /// </summary>
  [TestCase("nat-pmp", "NatPmp")]
  [TestCase("upnp", "Upnp")]
  [TestCase(null, null)]
  [TestCase("", null)]
  [TestCase("NAT-PMP", null)]
  [TestCase("natpmp", null)]
  [TestCase("other", null)]
  public void ParseStoredProtocol_RecognisesOnlyCanonicalValues(string? stored, string? expectedName)
  {
    var parsed = PortForwardingService.ParseStoredProtocol(stored);
    Assert.That(parsed?.ToString(), Is.EqualTo(expectedName));
  }
}
