using System.Net;
using System.Net.Http;
using System.Text;
using Server.Core.PortForwarding;

namespace Tests.Unit.Core;

[TestFixture]
public class IgdDiscoveryTests
{
  /// <summary>
  /// SCENARIO:
  /// Router returns a UPnP device description that declares the preferred
  /// WANIPConnection:2 service with a relative controlURL
  ///
  /// ACTION:
  /// IgdDiscovery.TryResolveAsync(descriptionUrl)
  ///
  /// EXPECTED RESULT:
  /// Returns an IgdEndpoint whose ControlUrl is resolved against the
  /// description URL's authority (not its path) and whose ServiceType
  /// matches the declared service
  /// </summary>
  [Test]
  public async Task TryResolveAsync_ResolvesRelativeControlUrl()
  {
    const string xml = """
      <root xmlns="urn:schemas-upnp-org:device-1-0">
        <device>
          <serviceList>
            <service>
              <serviceType>urn:schemas-upnp-org:service:WANIPConnection:2</serviceType>
              <controlURL>/ctl/IPConn</controlURL>
            </service>
          </serviceList>
        </device>
      </root>
      """;

    var http = FakeClient(xml);
    var discovery = new IgdDiscovery(http);

    var result = await discovery.TryResolveAsync(
      new Uri("http://192.168.1.1:49152/rootDesc.xml"), CancellationToken.None);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.ControlUrl, Is.EqualTo(new Uri("http://192.168.1.1:49152/ctl/IPConn")));
    Assert.That(result.ServiceType, Is.EqualTo("urn:schemas-upnp-org:service:WANIPConnection:2"));
    Assert.That(result.DeviceBaseUrl, Is.EqualTo("http://192.168.1.1:49152/"));
  }

  /// <summary>
  /// SCENARIO:
  /// Router declares only the legacy WANIPConnection:1 service; the newer
  /// WANIPConnection:2 is absent
  ///
  /// ACTION:
  /// IgdDiscovery.TryResolveAsync
  ///
  /// EXPECTED RESULT:
  /// Picks the legacy service rather than returning null - the discovery
  /// tries supported service types in preference order
  /// </summary>
  [Test]
  public async Task TryResolveAsync_FallsBackToLegacyServiceType()
  {
    const string xml = """
      <root xmlns="urn:schemas-upnp-org:device-1-0">
        <device>
          <serviceList>
            <service>
              <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
              <controlURL>/ipc/control</controlURL>
            </service>
          </serviceList>
        </device>
      </root>
      """;

    var http = FakeClient(xml);
    var discovery = new IgdDiscovery(http);

    var result = await discovery.TryResolveAsync(
      new Uri("http://10.0.0.1/desc.xml"), CancellationToken.None);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.ServiceType, Is.EqualTo("urn:schemas-upnp-org:service:WANIPConnection:1"));
  }

  /// <summary>
  /// SCENARIO:
  /// Description XML declares no WAN connection service - only unrelated
  /// services like the connection manager
  ///
  /// ACTION:
  /// IgdDiscovery.TryResolveAsync
  ///
  /// EXPECTED RESULT:
  /// Returns null so the caller can keep probing other candidate URLs
  /// </summary>
  [Test]
  public async Task TryResolveAsync_ReturnsNullWhenNoWanService()
  {
    const string xml = """
      <root xmlns="urn:schemas-upnp-org:device-1-0">
        <device>
          <serviceList>
            <service>
              <serviceType>urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1</serviceType>
              <controlURL>/unrelated</controlURL>
            </service>
          </serviceList>
        </device>
      </root>
      """;

    var http = FakeClient(xml);
    var discovery = new IgdDiscovery(http);

    var result = await discovery.TryResolveAsync(
      new Uri("http://10.0.0.1/desc.xml"), CancellationToken.None);

    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Device description body is malformed XML
  ///
  /// ACTION:
  /// IgdDiscovery.TryResolveAsync
  ///
  /// EXPECTED RESULT:
  /// Returns null - a broken router description must not surface as an
  /// exception to the outer probe loop
  /// </summary>
  [Test]
  public async Task TryResolveAsync_ReturnsNullOnMalformedXml()
  {
    var http = FakeClient("not xml <<");
    var discovery = new IgdDiscovery(http);

    var result = await discovery.TryResolveAsync(
      new Uri("http://10.0.0.1/desc.xml"), CancellationToken.None);

    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// HTTP request for the description returns a non-success status code
  ///
  /// ACTION:
  /// IgdDiscovery.TryResolveAsync
  ///
  /// EXPECTED RESULT:
  /// Returns null without throwing
  /// </summary>
  [Test]
  public async Task TryResolveAsync_ReturnsNullOnNonSuccessStatus()
  {
    var http = new HttpClient(new StubHandler("irrelevant", HttpStatusCode.NotFound));
    var discovery = new IgdDiscovery(http);

    var result = await discovery.TryResolveAsync(
      new Uri("http://10.0.0.1/desc.xml"), CancellationToken.None);

    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// SSDP M-SEARCH response text contains a LOCATION header and some
  /// irrelevant headers around it
  ///
  /// ACTION:
  /// IgdDiscovery.ExtractHeader
  ///
  /// EXPECTED RESULT:
  /// Returns the LOCATION value without surrounding whitespace and is
  /// case-insensitive on the header name
  /// </summary>
  [TestCase("LOCATION")]
  [TestCase("location")]
  [TestCase("Location")]
  public void ExtractHeader_IsCaseInsensitive(string queryName)
  {
    var response = "HTTP/1.1 200 OK\r\n"
                 + "CACHE-CONTROL: max-age=120\r\n"
                 + "LOCATION: http://192.168.1.1:49152/rootDesc.xml\r\n"
                 + "SERVER: router/1.0\r\n\r\n";

    var value = IgdDiscovery.ExtractHeader(response, queryName);

    Assert.That(value, Is.EqualTo("http://192.168.1.1:49152/rootDesc.xml"));
  }

  /// <summary>
  /// SCENARIO:
  /// SSDP response has no matching header
  ///
  /// ACTION:
  /// IgdDiscovery.ExtractHeader
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void ExtractHeader_ReturnsNullWhenAbsent()
  {
    var response = "HTTP/1.1 200 OK\r\nSERVER: foo\r\n\r\n";
    Assert.That(IgdDiscovery.ExtractHeader(response, "LOCATION"), Is.Null);
  }

  private static HttpClient FakeClient(string xml) =>
    new(new StubHandler(xml, HttpStatusCode.OK));

  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly string _body;
    private readonly HttpStatusCode _status;
    public StubHandler(string body, HttpStatusCode status) { _body = body; _status = status; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var response = new HttpResponseMessage(_status)
      {
        Content = new StringContent(_body, Encoding.UTF8, "text/xml")
      };
      return Task.FromResult(response);
    }
  }
}
