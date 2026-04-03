using System.Xml.Linq;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Cameras.Onvif.Services;

public sealed class AnalyticsService(SoapClient soap)
{
  public async Task<IReadOnlyList<AnalyticsModule>> GetAnalyticsModulesAsync(
    string analyticsUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsAnalytics + "GetAnalyticsModules");
    var response = await soap.SendAsync(analyticsUri, body, credentials, ct);

    var result = response.Element(
      XmlHelpers.NsAnalytics + "GetAnalyticsModulesResponse");
    if (result == null) return [];

    var modules = new List<AnalyticsModule>();
    foreach (var el in result.Elements(XmlHelpers.NsAnalytics + "AnalyticsModule"))
    {
      var name = el.Attribute("Name")?.Value;
      var type = el.Attribute("Type")?.Value;
      if (name == null || type == null) continue;

      int? rows = null, columns = null;
      var parameters = el.Element(XmlHelpers.NsSchema + "Parameters");
      if (parameters != null)
      {
        foreach (var item in parameters.Elements(XmlHelpers.NsSchema + "SimpleItem"))
        {
          var key = item.Attribute("Name")?.Value;
          var val = item.Attribute("Value")?.Value;
          if (key == "Rows" && int.TryParse(val, out var r)) rows = r;
          if (key == "Columns" && int.TryParse(val, out var c)) columns = c;
        }
      }

      modules.Add(new AnalyticsModule
      {
        Name = name,
        Type = type,
        Rows = rows,
        Columns = columns
      });
    }

    return modules;
  }

  public async Task<IReadOnlyList<AnalyticsModule>> GetRulesAsync(
    string analyticsUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsAnalytics + "GetRules");
    var response = await soap.SendAsync(analyticsUri, body, credentials, ct);

    var result = response.Element(XmlHelpers.NsAnalytics + "GetRulesResponse");
    if (result == null) return [];

    var modules = new List<AnalyticsModule>();
    foreach (var el in result.Elements(XmlHelpers.NsAnalytics + "Rule"))
    {
      var name = el.Attribute("Name")?.Value;
      var type = el.Attribute("Type")?.Value;
      if (name == null || type == null) continue;

      int? rows = null, columns = null;
      var parameters = el.Element(XmlHelpers.NsSchema + "Parameters");
      if (parameters != null)
      {
        foreach (var item in parameters.Elements(XmlHelpers.NsSchema + "SimpleItem"))
        {
          var key = item.Attribute("Name")?.Value;
          var val = item.Attribute("Value")?.Value;
          if (key == "Rows" && int.TryParse(val, out var r)) rows = r;
          if (key == "Columns" && int.TryParse(val, out var c)) columns = c;
        }
      }

      modules.Add(new AnalyticsModule
      {
        Name = name,
        Type = type,
        Rows = rows,
        Columns = columns
      });
    }

    return modules;
  }

  public async Task<string?> GetMetadataStreamUriAsync(
    string mediaUri, Credentials credentials, string profileToken, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsMedia + "GetMetadataConfigurations");
    XElement response;
    try
    {
      response = await soap.SendAsync(mediaUri, body, credentials, ct);
    }
    catch
    {
      return null;
    }

    var configs = response.Element(
      XmlHelpers.NsMedia + "GetMetadataConfigurationsResponse");
    if (configs == null) return null;

    var hasAnalytics = configs.Elements(XmlHelpers.NsMedia + "Configurations").Any(c =>
    {
      var analytics = c.Element(XmlHelpers.NsSchema + "Analytics");
      return analytics != null;
    });
    if (!hasAnalytics) return null;

    var streamBody = new XElement(XmlHelpers.NsMedia + "GetStreamUri",
      new XElement(XmlHelpers.NsMedia + "StreamSetup",
        new XElement(XmlHelpers.NsSchema + "Stream", "RTP-Unicast"),
        new XElement(XmlHelpers.NsSchema + "Transport",
          new XElement(XmlHelpers.NsSchema + "Protocol", "RTSP"))),
      new XElement(XmlHelpers.NsMedia + "ProfileToken", profileToken));
    var streamResponse = await soap.SendAsync(mediaUri, streamBody, credentials, ct);

    return streamResponse
      .Element(XmlHelpers.NsMedia + "GetStreamUriResponse")
      ?.Element(XmlHelpers.NsMedia + "MediaUri")
      ?.Element(XmlHelpers.NsSchema + "Uri")?.Value;
  }
}

public sealed class AnalyticsModule
{
  public required string Name { get; init; }
  public required string Type { get; init; }
  public int? Rows { get; init; }
  public int? Columns { get; init; }

  public bool IsCellMotionDetector =>
    Type.Contains("CellMotionDetector", StringComparison.OrdinalIgnoreCase)
    || Name.Contains("CellMotionDetector", StringComparison.OrdinalIgnoreCase);
}
