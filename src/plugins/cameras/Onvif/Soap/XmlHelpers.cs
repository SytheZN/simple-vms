using System.Xml.Linq;

namespace Cameras.Onvif.Soap;

public static class XmlHelpers
{
  public static readonly XNamespace NsSoap =
    "http://www.w3.org/2003/05/soap-envelope";
  public static readonly XNamespace NsWsa =
    "http://schemas.xmlsoap.org/ws/2004/08/addressing";
  public static readonly XNamespace NsWsa5 =
    "http://www.w3.org/2005/08/addressing";
  public static readonly XNamespace NsWsd =
    "http://schemas.xmlsoap.org/ws/2005/04/discovery";
  public static readonly XNamespace NsWsse =
    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
  public static readonly XNamespace NsWsu =
    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
  public static readonly XNamespace NsDevice =
    "http://www.onvif.org/ver10/device/wsdl";
  public static readonly XNamespace NsMedia =
    "http://www.onvif.org/ver10/media/wsdl";
  public static readonly XNamespace NsEvent =
    "http://www.onvif.org/ver10/events/wsdl";
  public static readonly XNamespace NsSchema =
    "http://www.onvif.org/ver10/schema";
  public static readonly XNamespace NsWsnt =
    "http://docs.oasis-open.org/wsn/b-2";
  public static readonly XNamespace NsDn =
    "http://www.onvif.org/ver10/network/wsdl";
  public static readonly XNamespace NsAnalytics =
    "http://www.onvif.org/ver20/analytics/wsdl";

  public static XDocument BuildEnvelope(
    XElement body, XElement? securityHeader = null, string? toAddress = null)
  {
    var header = new XElement(NsSoap + "Header");
    if (securityHeader != null)
      header.Add(securityHeader);
    if (toAddress != null)
      header.Add(new XElement(NsWsa5 + "To", toAddress));

    return new XDocument(
      new XElement(NsSoap + "Envelope",
        new XAttribute(XNamespace.Xmlns + "s", NsSoap),
        header,
        new XElement(NsSoap + "Body", body)));
  }

  public static XElement? GetBody(XDocument doc) =>
    doc.Root?.Element(NsSoap + "Body");

  public static XElement? GetFault(XDocument doc) =>
    GetBody(doc)?.Element(NsSoap + "Fault");
}
