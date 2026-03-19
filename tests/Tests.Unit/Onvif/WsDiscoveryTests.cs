using System.Xml.Linq;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;

namespace Tests.Unit.Onvif;

[TestFixture]
public class WsDiscoveryTests
{
  [Test]
  public void BuildProbeMessage_HasCorrectStructure()
  {
    var doc = WsDiscovery.BuildProbeMessage("urn:uuid:test-id");

    Assert.That(doc.Root!.Name, Is.EqualTo(XmlHelpers.NsSoap + "Envelope"));

    var header = doc.Root.Element(XmlHelpers.NsSoap + "Header");
    var messageId = header!.Element(XmlHelpers.NsWsa + "MessageID")!.Value;
    Assert.That(messageId, Is.EqualTo("urn:uuid:test-id"));

    var action = header.Element(XmlHelpers.NsWsa + "Action")!.Value;
    Assert.That(action, Is.EqualTo("http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe"));

    var body = doc.Root.Element(XmlHelpers.NsSoap + "Body");
    var probe = body!.Element(XmlHelpers.NsWsd + "Probe");
    Assert.That(probe, Is.Not.Null);

    var types = probe!.Element(XmlHelpers.NsWsd + "Types")!.Value;
    Assert.That(types, Is.EqualTo("dn:NetworkVideoTransmitter"));
  }

  [Test]
  public void ParseProbeMatch_ValidResponse_ExtractsAddresses()
  {
    var xml = $"""
      <s:Envelope xmlns:s="{XmlHelpers.NsSoap}"
                  xmlns:d="{XmlHelpers.NsWsd}">
        <s:Header/>
        <s:Body>
          <d:ProbeMatches>
            <d:ProbeMatch>
              <d:XAddrs>http://192.168.1.100:80/onvif/device_service</d:XAddrs>
            </d:ProbeMatch>
          </d:ProbeMatches>
        </s:Body>
      </s:Envelope>
      """;

    var addresses = WsDiscovery.ParseProbeMatch(xml).ToList();

    Assert.That(addresses, Has.Count.EqualTo(1));
    Assert.That(addresses[0], Is.EqualTo("http://192.168.1.100:80/onvif/device_service"));
  }

  [Test]
  public void ParseProbeMatch_MultipleXAddrs_ExtractsAll()
  {
    var xml = $"""
      <s:Envelope xmlns:s="{XmlHelpers.NsSoap}"
                  xmlns:d="{XmlHelpers.NsWsd}">
        <s:Header/>
        <s:Body>
          <d:ProbeMatches>
            <d:ProbeMatch>
              <d:XAddrs>http://192.168.1.100:80/onvif/device http://10.0.0.100:80/onvif/device</d:XAddrs>
            </d:ProbeMatch>
          </d:ProbeMatches>
        </s:Body>
      </s:Envelope>
      """;

    var addresses = WsDiscovery.ParseProbeMatch(xml).ToList();

    Assert.That(addresses, Has.Count.EqualTo(2));
  }

  [Test]
  public void ParseProbeMatch_EmptyBody_ReturnsEmpty()
  {
    var xml = $"""
      <s:Envelope xmlns:s="{XmlHelpers.NsSoap}">
        <s:Header/>
        <s:Body/>
      </s:Envelope>
      """;

    var addresses = WsDiscovery.ParseProbeMatch(xml).ToList();

    Assert.That(addresses, Is.Empty);
  }

  [Test]
  public void ParseProbeMatch_InvalidXml_ReturnsEmpty()
  {
    var addresses = WsDiscovery.ParseProbeMatch("not xml at all").ToList();

    Assert.That(addresses, Is.Empty);
  }

  [Test]
  public void ParseProbeMatch_MultipleDevices_ExtractsAll()
  {
    var xml = $"""
      <s:Envelope xmlns:s="{XmlHelpers.NsSoap}"
                  xmlns:d="{XmlHelpers.NsWsd}">
        <s:Header/>
        <s:Body>
          <d:ProbeMatches>
            <d:ProbeMatch>
              <d:XAddrs>http://192.168.1.100:80/onvif/device_service</d:XAddrs>
            </d:ProbeMatch>
            <d:ProbeMatch>
              <d:XAddrs>http://192.168.1.101:80/onvif/device_service</d:XAddrs>
            </d:ProbeMatch>
          </d:ProbeMatches>
        </s:Body>
      </s:Envelope>
      """;

    var addresses = WsDiscovery.ParseProbeMatch(xml).ToList();

    Assert.That(addresses, Has.Count.EqualTo(2));
  }
}
