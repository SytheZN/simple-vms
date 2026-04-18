using Client.Core.Tunnel;

namespace Tests.Unit.Client.Tunnel;

[TestFixture]
public class TlsTransportFactoryTests
{
  /// <summary>
  /// SCENARIO:
  /// IPv4 host with explicit port
  ///
  /// ACTION:
  /// ParseAddress("svms.example.com:9999")
  ///
  /// EXPECTED RESULT:
  /// Host = svms.example.com, Port = 9999
  /// </summary>
  [Test]
  public void ParseAddress_HostnameWithPort()
  {
    var (host, port) = TlsTransportFactory.ParseAddress("svms.example.com:9999");

    Assert.Multiple(() =>
    {
      Assert.That(host, Is.EqualTo("svms.example.com"));
      Assert.That(port, Is.EqualTo(9999));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// IPv4 host with no port supplied
  ///
  /// ACTION:
  /// ParseAddress("svms.example.com")
  ///
  /// EXPECTED RESULT:
  /// Host = svms.example.com, Port defaults to 4433
  /// </summary>
  [Test]
  public void ParseAddress_HostnameWithoutPort_Defaults()
  {
    var (host, port) = TlsTransportFactory.ParseAddress("svms.example.com");

    Assert.Multiple(() =>
    {
      Assert.That(host, Is.EqualTo("svms.example.com"));
      Assert.That(port, Is.EqualTo(4433));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// IPv4 dotted-quad with port
  ///
  /// ACTION:
  /// ParseAddress("192.168.1.10:5000")
  ///
  /// EXPECTED RESULT:
  /// Host = 192.168.1.10, Port = 5000
  /// </summary>
  [Test]
  public void ParseAddress_Ipv4WithPort()
  {
    var (host, port) = TlsTransportFactory.ParseAddress("192.168.1.10:5000");

    Assert.Multiple(() =>
    {
      Assert.That(host, Is.EqualTo("192.168.1.10"));
      Assert.That(port, Is.EqualTo(5000));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// IPv6 literal in brackets with port
  ///
  /// ACTION:
  /// ParseAddress("[::1]:8443")
  ///
  /// EXPECTED RESULT:
  /// Host = ::1, Port = 8443
  /// </summary>
  [Test]
  public void ParseAddress_Ipv6BracketsWithPort()
  {
    var (host, port) = TlsTransportFactory.ParseAddress("[::1]:8443");

    Assert.Multiple(() =>
    {
      Assert.That(host, Is.EqualTo("::1"));
      Assert.That(port, Is.EqualTo(8443));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// IPv6 literal in brackets without port
  ///
  /// ACTION:
  /// ParseAddress("[fe80::1]")
  ///
  /// EXPECTED RESULT:
  /// Host = fe80::1, Port defaults to 4433
  /// </summary>
  [Test]
  public void ParseAddress_Ipv6BracketsNoPort_Defaults()
  {
    var (host, port) = TlsTransportFactory.ParseAddress("[fe80::1]");

    Assert.Multiple(() =>
    {
      Assert.That(host, Is.EqualTo("fe80::1"));
      Assert.That(port, Is.EqualTo(4433));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Hostname with a port that fails to parse as an integer
  ///
  /// ACTION:
  /// ParseAddress("svms.example.com:notaport")
  ///
  /// EXPECTED RESULT:
  /// Host parses; port falls back to 4433 default
  /// </summary>
  [Test]
  public void ParseAddress_BadPortString_Defaults()
  {
    var (host, port) = TlsTransportFactory.ParseAddress("svms.example.com:notaport");

    Assert.Multiple(() =>
    {
      Assert.That(host, Is.EqualTo("svms.example.com"));
      Assert.That(port, Is.EqualTo(4433));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Bracketed IPv6 with malformed close-bracket missing
  ///
  /// ACTION:
  /// ParseAddress("[::1") (no closing bracket)
  ///
  /// EXPECTED RESULT:
  /// The whole string is returned as host; port defaults
  /// </summary>
  [Test]
  public void ParseAddress_Ipv6MissingCloseBracket_FallsBack()
  {
    var (host, port) = TlsTransportFactory.ParseAddress("[::1");

    Assert.Multiple(() =>
    {
      Assert.That(host, Is.EqualTo("[::1"));
      Assert.That(port, Is.EqualTo(4433));
    });
  }
}
