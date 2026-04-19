using Server.Core.Services;
using Shared.Models.Dto;

namespace Tests.Unit.Core;

[TestFixture]
public class SystemServiceValidationTests
{
  /// <summary>
  /// SCENARIO:
  /// Admin supplies an internal endpoint that resolves to loopback, the Docker host bridge name,
  /// or an IPv6 link-local address
  ///
  /// ACTION:
  /// ValidateInternalEndpoint(endpoint)
  ///
  /// EXPECTED RESULT:
  /// Rejected - other devices on the network cannot reach such an address
  /// </summary>
  [TestCase("localhost")]
  [TestCase("LOCALHOST")]
  [TestCase("127.0.0.1")]
  [TestCase("127.0.0.1:4433")]
  [TestCase("::1")]
  [TestCase("host.docker.internal")]
  [TestCase("host.docker.internal:8443")]
  public void ValidateInternalEndpoint_RejectsUnreachableHosts(string endpoint)
  {
    Assert.That(SystemService.ValidateInternalEndpoint(endpoint).IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Admin supplies an internal endpoint that is a routable LAN IP or a valid hostname,
  /// with or without an explicit port
  ///
  /// ACTION:
  /// ValidateInternalEndpoint(endpoint)
  ///
  /// EXPECTED RESULT:
  /// Accepted
  /// </summary>
  [TestCase("192.168.1.50")]
  [TestCase("vms.local")]
  [TestCase("10.0.0.4:4433")]
  [TestCase("vms.home.arpa:12345")]
  public void ValidateInternalEndpoint_AcceptsRoutableHosts(string endpoint)
  {
    Assert.That(SystemService.ValidateInternalEndpoint(endpoint).IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Admin supplies an internal endpoint whose port portion is out of range or not a number
  ///
  /// ACTION:
  /// ValidateInternalEndpoint(endpoint)
  ///
  /// EXPECTED RESULT:
  /// Rejected with a port-specific error
  /// </summary>
  [TestCase("192.168.1.50:0")]
  [TestCase("192.168.1.50:70000")]
  [TestCase("192.168.1.50:abc")]
  public void ValidateInternalEndpoint_RejectsInvalidPort(string endpoint)
  {
    Assert.That(SystemService.ValidateInternalEndpoint(endpoint).IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Admin submits an empty or whitespace-only internal endpoint
  ///
  /// ACTION:
  /// ValidateInternalEndpoint(endpoint)
  ///
  /// EXPECTED RESULT:
  /// Rejected
  /// </summary>
  [Test]
  public void ValidateInternalEndpoint_RejectsEmpty()
  {
    Assert.That(SystemService.ValidateInternalEndpoint("").IsT1, Is.True);
    Assert.That(SystemService.ValidateInternalEndpoint("   ").IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Admin supplies an external host that is a routable hostname or public IP literal
  ///
  /// ACTION:
  /// ValidateExternalHost(host)
  ///
  /// EXPECTED RESULT:
  /// Accepted
  /// </summary>
  [TestCase("myhome.ddns.net")]
  [TestCase("203.0.113.42")]
  [TestCase("1.2.3.4")]
  public void ValidateExternalHost_AcceptsRoutableHosts(string host)
  {
    Assert.That(SystemService.ValidateExternalHost(host).IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Admin supplies an external host that is unreachable from the internet, empty,
  /// or already includes a port (the port is a separate field)
  ///
  /// ACTION:
  /// ValidateExternalHost(host)
  ///
  /// EXPECTED RESULT:
  /// Rejected
  /// </summary>
  [TestCase("localhost")]
  [TestCase("127.0.0.1")]
  [TestCase("host.docker.internal")]
  [TestCase("")]
  [TestCase("myhome.ddns.net:443")]
  public void ValidateExternalHost_Rejects(string host)
  {
    Assert.That(SystemService.ValidateExternalHost(host).IsT1, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Admin submits an external port alongside a mode (manual or upnp)
  ///
  /// ACTION:
  /// ValidateExternalPort(port, mode)
  ///
  /// EXPECTED RESULT:
  /// UPnP mode enforces 20000-60000 as a safety policy; manual mode allows any valid TCP port 1-65535
  /// </summary>
  [TestCase(20000, RemoteAccessMode.Upnp, true)]
  [TestCase(34567, RemoteAccessMode.Upnp, true)]
  [TestCase(60000, RemoteAccessMode.Upnp, true)]
  [TestCase(19999, RemoteAccessMode.Upnp, false)]
  [TestCase(60001, RemoteAccessMode.Upnp, false)]
  [TestCase(443, RemoteAccessMode.Upnp, false)]
  [TestCase(443, RemoteAccessMode.Manual, true)]
  [TestCase(1, RemoteAccessMode.Manual, true)]
  [TestCase(65535, RemoteAccessMode.Manual, true)]
  [TestCase(0, RemoteAccessMode.Manual, false)]
  [TestCase(65536, RemoteAccessMode.Manual, false)]
  public void ValidateExternalPort_EnforcesRangeByMode(int port, RemoteAccessMode mode, bool expectValid)
  {
    Assert.That(SystemService.ValidateExternalPort(port, mode).IsT0, Is.EqualTo(expectValid));
  }

  /// <summary>
  /// SCENARIO:
  /// Admin supplies a router address for UPnP mode
  ///
  /// ACTION:
  /// ValidateRouterAddress(address)
  ///
  /// EXPECTED RESULT:
  /// Accepts IPv4 literals and hostnames (resolved at reconcile time); rejects IPv6 literals,
  /// empty input, and malformed strings
  /// </summary>
  [TestCase("192.168.1.1", true)]
  [TestCase("10.0.0.1", true)]
  [TestCase("pfsense.home.arpa", true)]
  [TestCase("router", true)]
  [TestCase("", false)]
  [TestCase("::1", false)]
  [TestCase("not a valid hostname!", false)]
  public void ValidateRouterAddress_AcceptsIPv4OrHostname(string address, bool expectValid)
  {
    Assert.That(SystemService.ValidateRouterAddress(address).IsT0, Is.EqualTo(expectValid));
  }

  /// <summary>
  /// SCENARIO:
  /// Configuration contains an explicit remoteAccess.mode key
  ///
  /// ACTION:
  /// InferMode(settings)
  ///
  /// EXPECTED RESULT:
  /// Returns the explicitly configured mode
  /// </summary>
  [Test]
  public void InferMode_UsesExplicitKey()
  {
    var settings = new Dictionary<string, string>
    {
      ["server.remoteAccess.mode"] = "upnp",
      ["server.internalEndpoint"] = "192.168.1.50"
    };
    Assert.That(SystemService.InferMode(settings), Is.EqualTo(RemoteAccessMode.Upnp));
  }

  /// <summary>
  /// SCENARIO:
  /// Pre-redesign install persisted the legacy server.upnp.enabled boolean
  ///
  /// ACTION:
  /// InferMode(settings)
  ///
  /// EXPECTED RESULT:
  /// Returns Upnp so the existing mapping keeps running until the user saves
  /// </summary>
  [Test]
  public void InferMode_InfersUpnpFromLegacyFlag()
  {
    var settings = new Dictionary<string, string>
    {
      ["server.upnp.enabled"] = "true"
    };
    Assert.That(SystemService.InferMode(settings), Is.EqualTo(RemoteAccessMode.Upnp));
  }

  /// <summary>
  /// SCENARIO:
  /// Install has the split external host/port fields but no explicit mode
  ///
  /// ACTION:
  /// InferMode(settings)
  ///
  /// EXPECTED RESULT:
  /// Returns Manual - the split fields by themselves imply user-managed forwarding
  /// </summary>
  [Test]
  public void InferMode_InfersManualFromSplitFields()
  {
    var settings = new Dictionary<string, string>
    {
      ["server.externalHost"] = "myhome.ddns.net",
      ["server.externalPort"] = "443"
    };
    Assert.That(SystemService.InferMode(settings), Is.EqualTo(RemoteAccessMode.Manual));
  }

  /// <summary>
  /// SCENARIO:
  /// Install still has the pre-redesign externalEndpoint key and no new split fields
  ///
  /// ACTION:
  /// InferMode(settings)
  ///
  /// EXPECTED RESULT:
  /// Returns null so the upgrade wizard handles the migration
  /// </summary>
  [Test]
  public void InferMode_ReturnsNullWhenLegacyEndpointStillPresent()
  {
    var settings = new Dictionary<string, string>
    {
      ["server.externalEndpoint"] = "myhome.ddns.net:443"
    };
    Assert.That(SystemService.InferMode(settings), Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Install has no persisted mode and no remote-access markers (fresh install,
  /// or a partially configured server with only an internal endpoint)
  ///
  /// ACTION:
  /// InferMode(settings)
  ///
  /// EXPECTED RESULT:
  /// Returns None so callers and the UI see a definite default instead of null
  /// </summary>
  [TestCase("192.168.1.50")]
  [TestCase(null)]
  public void InferMode_DefaultsToNone(string? internalEndpoint)
  {
    var settings = new Dictionary<string, string>();
    if (internalEndpoint != null)
      settings["server.internalEndpoint"] = internalEndpoint;

    Assert.That(SystemService.InferMode(settings), Is.EqualTo(RemoteAccessMode.None));
  }

  /// <summary>
  /// SCENARIO:
  /// Fresh install has no configuration at all
  ///
  /// ACTION:
  /// ComputeMissingSettings(settings)
  ///
  /// EXPECTED RESULT:
  /// Lists internalEndpoint so the upgrade-required gate drives the wizard
  /// </summary>
  [Test]
  public void ComputeMissingSettings_ListsInternalEndpointWhenAbsent()
  {
    var settings = new Dictionary<string, string>();
    var missing = SystemService.ComputeMissingSettings(settings);
    Assert.That(missing, Contains.Item("internalEndpoint"));
  }

  /// <summary>
  /// SCENARIO:
  /// Install still has the pre-redesign externalEndpoint key
  ///
  /// ACTION:
  /// ComputeMissingSettings(settings)
  ///
  /// EXPECTED RESULT:
  /// Flags legacyExternalEndpoint so the wizard can pre-fill and migrate
  /// </summary>
  [Test]
  public void ComputeMissingSettings_FlagsLegacyEndpoint()
  {
    var settings = new Dictionary<string, string>
    {
      ["server.internalEndpoint"] = "192.168.1.50",
      ["server.externalEndpoint"] = "myhome.ddns.net:443"
    };
    var missing = SystemService.ComputeMissingSettings(settings);
    Assert.That(missing, Contains.Item("legacyExternalEndpoint"));
  }

  /// <summary>
  /// SCENARIO:
  /// Install has an internal endpoint and an explicit None mode
  ///
  /// ACTION:
  /// ComputeMissingSettings(settings)
  ///
  /// EXPECTED RESULT:
  /// Returns null - nothing is missing
  /// </summary>
  [Test]
  public void ComputeMissingSettings_NullWhenEverythingPresent()
  {
    var settings = new Dictionary<string, string>
    {
      ["server.internalEndpoint"] = "192.168.1.50",
      ["server.remoteAccess.mode"] = "none"
    };
    Assert.That(SystemService.ComputeMissingSettings(settings), Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Install declares Upnp mode but has not supplied the external port or router address
  ///
  /// ACTION:
  /// ComputeMissingSettings(settings)
  ///
  /// EXPECTED RESULT:
  /// Lists each missing field so the wizard can prompt the user
  /// </summary>
  [Test]
  public void ComputeMissingSettings_FlagsIncompleteUpnpConfig()
  {
    var settings = new Dictionary<string, string>
    {
      ["server.internalEndpoint"] = "192.168.1.50",
      ["server.remoteAccess.mode"] = "upnp",
      ["server.externalHost"] = "myhome.ddns.net"
    };
    var missing = SystemService.ComputeMissingSettings(settings);
    Assert.That(missing, Contains.Item("externalPort"));
    Assert.That(missing, Contains.Item("upnpRouterAddress"));
  }
}
