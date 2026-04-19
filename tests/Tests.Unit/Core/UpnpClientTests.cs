using System.Net;
using Server.Core.PortForwarding;

namespace Tests.Unit.Core;

[TestFixture]
public class UpnpClientTests
{
  private const string ServiceType = "urn:schemas-upnp-org:service:WANIPConnection:1";
  private static readonly Uri ControlUrl = new("http://127.0.0.1/ctl/IPConn");

  private sealed class StubHandler : HttpMessageHandler
  {
    public string? LastSoapAction { get; private set; }
    public string? LastBody { get; private set; }
    public required Func<HttpRequestMessage, HttpResponseMessage> Respond { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken ct)
    {
      LastSoapAction = request.Headers.GetValues("SOAPAction").FirstOrDefault();
      LastBody = request.Content != null ? await request.Content.ReadAsStringAsync(ct) : null;
      return Respond(request);
    }
  }

  private static HttpResponseMessage OkXml(string xml) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(xml, System.Text.Encoding.UTF8, "text/xml")
  };

  /// <summary>
  /// SCENARIO:
  /// Router returns a well-formed GetExternalIPAddress SOAP response
  ///
  /// ACTION:
  /// UpnpClient.GetExternalIPAsync
  ///
  /// EXPECTED RESULT:
  /// Returns the WAN IP address from NewExternalIPAddress and sets the correct SOAPAction header
  /// </summary>
  [Test]
  public async Task GetExternalIPAsync_ParsesAddressFromResponse()
  {
    var handler = new StubHandler
    {
      Respond = _ => OkXml("""
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body>
            <u:GetExternalIPAddressResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1">
              <NewExternalIPAddress>203.0.113.42</NewExternalIPAddress>
            </u:GetExternalIPAddressResponse>
          </s:Body>
        </s:Envelope>
        """)
    };
    var client = new UpnpClient(new HttpClient(handler), ControlUrl, ServiceType);

    var result = await client.GetExternalIPAsync(CancellationToken.None);

    Assert.That(result, Is.EqualTo("203.0.113.42"));
    Assert.That(handler.LastSoapAction,
      Is.EqualTo($"\"{ServiceType}#GetExternalIPAddress\""));
  }

  /// <summary>
  /// SCENARIO:
  /// Port-forwarding service maps an external port to the tunnel port on the LAN server
  ///
  /// ACTION:
  /// UpnpClient.AddPortMappingAsync with concrete port/client/lease values
  ///
  /// EXPECTED RESULT:
  /// SOAP envelope contains the supplied values and the SOAPAction header names AddPortMapping
  /// </summary>
  [Test]
  public async Task AddPortMappingAsync_SendsExpectedEnvelope()
  {
    var handler = new StubHandler
    {
      Respond = _ => OkXml("""
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body>
            <u:AddPortMappingResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1"/>
          </s:Body>
        </s:Envelope>
        """)
    };
    var client = new UpnpClient(new HttpClient(handler), ControlUrl, ServiceType);

    await client.AddPortMappingAsync(
      externalPort: 30000, internalPort: 4433,
      internalClient: "192.168.1.50", leaseSeconds: 3600,
      description: "Simple VMS tunnel", CancellationToken.None);

    Assert.That(handler.LastSoapAction,
      Is.EqualTo($"\"{ServiceType}#AddPortMapping\""));
    Assert.That(handler.LastBody, Does.Contain("<NewExternalPort>30000</NewExternalPort>"));
    Assert.That(handler.LastBody, Does.Contain("<NewInternalPort>4433</NewInternalPort>"));
    Assert.That(handler.LastBody, Does.Contain("<NewInternalClient>192.168.1.50</NewInternalClient>"));
    Assert.That(handler.LastBody, Does.Contain("<NewLeaseDuration>3600</NewLeaseDuration>"));
  }

  /// <summary>
  /// SCENARIO:
  /// Router rejects the AddPortMapping with a SOAP Fault carrying a UPnPError code and description
  ///
  /// ACTION:
  /// UpnpClient.AddPortMappingAsync
  ///
  /// EXPECTED RESULT:
  /// Throws UpnpSoapFaultException preserving the code, description, and raw fault body for logging
  /// </summary>
  [Test]
  public void AddPortMappingAsync_PropagatesFaultWithFullDetail()
  {
    var fault = """
      <?xml version="1.0"?>
      <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
        <s:Body>
          <s:Fault>
            <faultcode>s:Client</faultcode>
            <faultstring>UPnPError</faultstring>
            <detail>
              <UPnPError xmlns="urn:schemas-upnp-org:control-1-0">
                <errorCode>718</errorCode>
                <errorDescription>ConflictInMappingEntry</errorDescription>
              </UPnPError>
            </detail>
          </s:Fault>
        </s:Body>
      </s:Envelope>
      """;
    var handler = new StubHandler
    {
      Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
      {
        Content = new StringContent(fault, System.Text.Encoding.UTF8, "text/xml")
      }
    };
    var client = new UpnpClient(new HttpClient(handler), ControlUrl, ServiceType);

    var ex = Assert.ThrowsAsync<UpnpSoapFaultException>(() =>
      client.AddPortMappingAsync(30000, 4433, "192.168.1.50", 3600, "test", CancellationToken.None));

    Assert.That(ex!.ErrorCode, Is.EqualTo(718));
    Assert.That(ex.ErrorDescription, Is.EqualTo("ConflictInMappingEntry"));
    Assert.That(ex.RawFault, Does.Contain("ConflictInMappingEntry"));
  }

  /// <summary>
  /// SCENARIO:
  /// Port-forwarding service tears down a previously active mapping
  ///
  /// ACTION:
  /// UpnpClient.DeletePortMappingAsync
  ///
  /// EXPECTED RESULT:
  /// SOAPAction header names DeletePortMapping and the envelope carries the external port to remove
  /// </summary>
  [Test]
  public async Task DeletePortMappingAsync_SendsExpectedAction()
  {
    var handler = new StubHandler
    {
      Respond = _ => OkXml("""
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body>
            <u:DeletePortMappingResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1"/>
          </s:Body>
        </s:Envelope>
        """)
    };
    var client = new UpnpClient(new HttpClient(handler), ControlUrl, ServiceType);

    await client.DeletePortMappingAsync(30000, CancellationToken.None);

    Assert.That(handler.LastSoapAction,
      Is.EqualTo($"\"{ServiceType}#DeletePortMapping\""));
    Assert.That(handler.LastBody, Does.Contain("<NewExternalPort>30000</NewExternalPort>"));
  }
}
