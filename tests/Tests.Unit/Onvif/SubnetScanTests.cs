using System.Net;
using Cameras.Onvif.Services;

namespace Tests.Unit.Onvif;

[TestFixture]
public class SubnetScanTests
{
  [Test]
  public void EnumerateSubnet_Slash24_Returns254Hosts()
  {
    var hosts = WsDiscovery.EnumerateSubnet("192.168.1.0/24").ToList();

    Assert.That(hosts, Has.Count.EqualTo(254));
    Assert.That(hosts[0], Is.EqualTo(IPAddress.Parse("192.168.1.1")));
    Assert.That(hosts[^1], Is.EqualTo(IPAddress.Parse("192.168.1.254")));
  }

  [Test]
  public void EnumerateSubnet_Slash24_ExcludesNetworkAndBroadcast()
  {
    var hosts = WsDiscovery.EnumerateSubnet("10.0.0.0/24").ToList();

    Assert.That(hosts, Does.Not.Contain(IPAddress.Parse("10.0.0.0")));
    Assert.That(hosts, Does.Not.Contain(IPAddress.Parse("10.0.0.255")));
  }

  [Test]
  public void EnumerateSubnet_Slash32_ReturnsSingleHost()
  {
    var hosts = WsDiscovery.EnumerateSubnet("192.168.1.100/32").ToList();

    Assert.That(hosts, Has.Count.EqualTo(1));
    Assert.That(hosts[0], Is.EqualTo(IPAddress.Parse("192.168.1.100")));
  }

  [Test]
  public void EnumerateSubnet_Slash16_Returns65534Hosts()
  {
    var hosts = WsDiscovery.EnumerateSubnet("172.16.0.0/16").ToList();

    Assert.That(hosts, Has.Count.EqualTo(65534));
    Assert.That(hosts[0], Is.EqualTo(IPAddress.Parse("172.16.0.1")));
    Assert.That(hosts[^1], Is.EqualTo(IPAddress.Parse("172.16.255.254")));
  }

  [Test]
  public void EnumerateSubnet_NoCidr_TreatedAsSlash32()
  {
    var hosts = WsDiscovery.EnumerateSubnet("192.168.1.50").ToList();

    Assert.That(hosts, Has.Count.EqualTo(1));
    Assert.That(hosts[0], Is.EqualTo(IPAddress.Parse("192.168.1.50")));
  }

  [Test]
  public void EnumerateSubnet_InvalidAddress_ReturnsEmpty()
  {
    var hosts = WsDiscovery.EnumerateSubnet("not-an-ip/24").ToList();

    Assert.That(hosts, Is.Empty);
  }

  [Test]
  public void EnumerateSubnet_InvalidPrefix_ReturnsEmpty()
  {
    var hosts = WsDiscovery.EnumerateSubnet("192.168.1.0/33").ToList();

    Assert.That(hosts, Is.Empty);
  }

  [Test]
  public void EnumerateSubnet_Slash28_Returns14Hosts()
  {
    var hosts = WsDiscovery.EnumerateSubnet("192.168.1.0/28").ToList();

    Assert.That(hosts, Has.Count.EqualTo(14));
    Assert.That(hosts[0], Is.EqualTo(IPAddress.Parse("192.168.1.1")));
    Assert.That(hosts[^1], Is.EqualTo(IPAddress.Parse("192.168.1.14")));
  }
}
