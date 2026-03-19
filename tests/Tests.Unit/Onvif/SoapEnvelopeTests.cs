using System.Xml.Linq;
using Cameras.Onvif.Soap;

namespace Tests.Unit.Onvif;

[TestFixture]
public class SoapEnvelopeTests
{
  [Test]
  public void BuildEnvelope_WithBody_ProducesValidSoapStructure()
  {
    var body = new XElement(XmlHelpers.NsDevice + "GetDeviceInformation");

    var doc = XmlHelpers.BuildEnvelope(body);

    Assert.That(doc.Root, Is.Not.Null);
    Assert.That(doc.Root!.Name, Is.EqualTo(XmlHelpers.NsSoap + "Envelope"));

    var header = doc.Root.Element(XmlHelpers.NsSoap + "Header");
    Assert.That(header, Is.Not.Null);

    var soapBody = doc.Root.Element(XmlHelpers.NsSoap + "Body");
    Assert.That(soapBody, Is.Not.Null);

    var inner = soapBody!.Element(XmlHelpers.NsDevice + "GetDeviceInformation");
    Assert.That(inner, Is.Not.Null);
  }

  [Test]
  public void BuildEnvelope_WithSecurity_IncludesSecurityHeader()
  {
    var body = new XElement(XmlHelpers.NsDevice + "GetDeviceInformation");
    var security = WsUsernameToken.Build("admin", "pass", new byte[16], DateTime.UtcNow);

    var doc = XmlHelpers.BuildEnvelope(body, security);

    var header = doc.Root!.Element(XmlHelpers.NsSoap + "Header");
    var sec = header!.Element(XmlHelpers.NsWsse + "Security");
    Assert.That(sec, Is.Not.Null);

    var token = sec!.Element(XmlHelpers.NsWsse + "UsernameToken");
    Assert.That(token, Is.Not.Null);
    Assert.That(token!.Element(XmlHelpers.NsWsse + "Username")!.Value, Is.EqualTo("admin"));
  }

  [Test]
  public void GetBody_ParsesResponseEnvelope()
  {
    var xml = $"""
      <s:Envelope xmlns:s="{XmlHelpers.NsSoap}">
        <s:Header/>
        <s:Body>
          <TestResponse>OK</TestResponse>
        </s:Body>
      </s:Envelope>
      """;
    var doc = XDocument.Parse(xml);

    var body = XmlHelpers.GetBody(doc);

    Assert.That(body, Is.Not.Null);
    Assert.That(body!.Element("TestResponse")!.Value, Is.EqualTo("OK"));
  }

  [Test]
  public void GetFault_ReturnsFaultElement()
  {
    var xml = $"""
      <s:Envelope xmlns:s="{XmlHelpers.NsSoap}">
        <s:Header/>
        <s:Body>
          <s:Fault>
            <s:Reason><s:Text>Auth failed</s:Text></s:Reason>
          </s:Fault>
        </s:Body>
      </s:Envelope>
      """;
    var doc = XDocument.Parse(xml);

    var fault = XmlHelpers.GetFault(doc);

    Assert.That(fault, Is.Not.Null);
    var reason = fault!
      .Element(XmlHelpers.NsSoap + "Reason")!
      .Element(XmlHelpers.NsSoap + "Text")!.Value;
    Assert.That(reason, Is.EqualTo("Auth failed"));
  }

  [Test]
  public void RoundTrip_BuildThenParse()
  {
    var body = new XElement(XmlHelpers.NsMedia + "GetProfiles");
    var doc = XmlHelpers.BuildEnvelope(body);
    var reparsed = XDocument.Parse(doc.ToString());

    var parsedBody = XmlHelpers.GetBody(reparsed);
    Assert.That(parsedBody, Is.Not.Null);
    Assert.That(parsedBody!.Element(XmlHelpers.NsMedia + "GetProfiles"), Is.Not.Null);
  }
}
