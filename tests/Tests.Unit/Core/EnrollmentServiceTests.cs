using Server.Core;

namespace Tests.Unit.Core;

[TestFixture]
public class EnrollmentServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// Enrollment addresses may arrive as bare host, host:port, or hostname:port
  ///
  /// ACTION:
  /// HostPort.NormalizeEndpoint(input, defaultPort)
  ///
  /// EXPECTED RESULT:
  /// Appends the default port when missing; preserves any explicit port supplied by the user
  /// </summary>
  [TestCase("192.168.1.50", 4433, "192.168.1.50:4433")]
  [TestCase("vms.local", 4433, "vms.local:4433")]
  [TestCase("10.0.0.1:9000", 4433, "10.0.0.1:9000")]
  [TestCase("myhome.ddns.net:443", 4433, "myhome.ddns.net:443")]
  [TestCase("[::1]", 4433, "[::1]:4433")]
  [TestCase("[fe80::1]:9000", 4433, "[fe80::1]:9000")]
  [TestCase("fe80::1", 4433, "[fe80::1]:4433")]
  public void NormalizeEndpoint_AppendsPortWhenMissing(
    string input, int defaultPort, string expected)
  {
    Assert.That(HostPort.NormalizeEndpoint(input, defaultPort), Is.EqualTo(expected));
  }
}
