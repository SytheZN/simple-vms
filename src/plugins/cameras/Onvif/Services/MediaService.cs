using System.Xml.Linq;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Cameras.Onvif.Services;

public sealed class MediaService(SoapClient soap)
{
  public async Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(
    string mediaUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsMedia + "GetProfiles");
    var response = await soap.SendAsync(mediaUri, body, credentials, ct);

    var profilesEl = response.Element(XmlHelpers.NsMedia + "GetProfilesResponse");
    if (profilesEl == null) return [];

    var profiles = new List<OnvifProfile>();
    foreach (var p in profilesEl.Elements(XmlHelpers.NsMedia + "Profiles"))
    {
      var token = p.Attribute("token")?.Value;
      if (string.IsNullOrEmpty(token)) continue;

      var videoEncoder = p.Element(XmlHelpers.NsSchema + "VideoEncoderConfiguration");
      var resolution = videoEncoder?.Element(XmlHelpers.NsSchema + "Resolution");

      profiles.Add(new OnvifProfile
      {
        Token = token,
        Name = p.Attribute("fixed")?.Value != "true"
          ? p.Element(XmlHelpers.NsSchema + "Name")?.Value : null,
        Codec = ParseCodec(videoEncoder?.Element(XmlHelpers.NsSchema + "Encoding")?.Value),
        Width = int.TryParse(resolution?.Element(XmlHelpers.NsSchema + "Width")?.Value, out var w) ? w : null,
        Height = int.TryParse(resolution?.Element(XmlHelpers.NsSchema + "Height")?.Value, out var h) ? h : null,
        Fps = int.TryParse(
          videoEncoder?.Element(XmlHelpers.NsSchema + "RateControl")
            ?.Element(XmlHelpers.NsSchema + "FrameRateLimit")?.Value, out var fps) ? fps : null,
        Bitrate = int.TryParse(
          videoEncoder?.Element(XmlHelpers.NsSchema + "RateControl")
            ?.Element(XmlHelpers.NsSchema + "BitrateLimit")?.Value, out var br) ? br : null,
        HasPtzBinding = HasMovablePtzConfiguration(p.Element(XmlHelpers.NsSchema + "PTZConfiguration"))
      });
    }

    return profiles;
  }

  public async Task<string?> GetStreamUriAsync(
    string mediaUri, Credentials credentials, string profileToken, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsMedia + "GetStreamUri",
      new XElement(XmlHelpers.NsMedia + "StreamSetup",
        new XElement(XmlHelpers.NsSchema + "Stream", "RTP-Unicast"),
        new XElement(XmlHelpers.NsSchema + "Transport",
          new XElement(XmlHelpers.NsSchema + "Protocol", "RTSP"))),
      new XElement(XmlHelpers.NsMedia + "ProfileToken", profileToken));
    var response = await soap.SendAsync(mediaUri, body, credentials, ct);

    return response
      .Element(XmlHelpers.NsMedia + "GetStreamUriResponse")
      ?.Element(XmlHelpers.NsMedia + "MediaUri")
      ?.Element(XmlHelpers.NsSchema + "Uri")?.Value;
  }

  public static StreamProfile ToStreamProfile(OnvifProfile profile, string streamUri, int index) =>
    new()
    {
      Profile = index == 0 ? "main" : index == 1 ? "sub" : $"stream{index}",
      Kind = StreamKind.Quality,
      FormatId = "fmp4",
      Codec = profile.Codec,
      Resolution = profile.Width.HasValue && profile.Height.HasValue
        ? $"{profile.Width}x{profile.Height}" : null,
      Fps = profile.Fps,
      Bitrate = profile.Bitrate,
      Uri = streamUri
    };

  public async Task<IReadOnlyList<OnvifProfile>> GetProfilesV2Async(
    string media2Uri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsMedia2 + "GetProfiles",
      new XElement(XmlHelpers.NsMedia2 + "Type", "All"));
    var response = await soap.SendAsync(media2Uri, body, credentials, ct);

    var profilesEl = response.Element(XmlHelpers.NsMedia2 + "GetProfilesResponse");
    if (profilesEl == null) return [];

    var profiles = new List<OnvifProfile>();
    foreach (var p in profilesEl.Elements(XmlHelpers.NsMedia2 + "Profiles"))
    {
      var token = p.Attribute("token")?.Value;
      if (string.IsNullOrEmpty(token)) continue;

      var configurations = p.Element(XmlHelpers.NsMedia2 + "Configurations");
      var videoEncoder = configurations?.Element(XmlHelpers.NsMedia2 + "VideoEncoder");
      var resolution = videoEncoder?.Element(XmlHelpers.NsSchema + "Resolution");

      profiles.Add(new OnvifProfile
      {
        Token = token,
        Name = p.Attribute("fixed")?.Value != "true"
          ? p.Element(XmlHelpers.NsMedia2 + "Name")?.Value : null,
        Codec = ParseCodec(videoEncoder?.Element(XmlHelpers.NsSchema + "Encoding")?.Value),
        Width = int.TryParse(resolution?.Element(XmlHelpers.NsSchema + "Width")?.Value, out var w) ? w : null,
        Height = int.TryParse(resolution?.Element(XmlHelpers.NsSchema + "Height")?.Value, out var h) ? h : null,
        Fps = int.TryParse(
          videoEncoder?.Element(XmlHelpers.NsSchema + "RateControl")
            ?.Element(XmlHelpers.NsSchema + "FrameRateLimit")?.Value, out var fps) ? fps : null,
        Bitrate = int.TryParse(
          videoEncoder?.Element(XmlHelpers.NsSchema + "RateControl")
            ?.Element(XmlHelpers.NsSchema + "BitrateLimit")?.Value, out var br) ? br : null,
        HasPtzBinding = HasMovablePtzConfiguration(configurations?.Element(XmlHelpers.NsMedia2 + "PTZ"))
      });
    }

    return profiles;
  }

  public async Task<string?> GetStreamUriV2Async(
    string media2Uri, Credentials credentials, string profileToken, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsMedia2 + "GetStreamUri",
      new XElement(XmlHelpers.NsMedia2 + "Protocol", "RTSP"),
      new XElement(XmlHelpers.NsMedia2 + "ProfileToken", profileToken));
    var response = await soap.SendAsync(media2Uri, body, credentials, ct);

    return response
      .Element(XmlHelpers.NsMedia2 + "GetStreamUriResponse")
      ?.Element(XmlHelpers.NsMedia2 + "Uri")?.Value;
  }

  public async Task<bool> HasVideoAnalyticsConfigurationAsync(
    string mediaUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsMedia + "GetVideoAnalyticsConfigurations");
    var response = await soap.SendAsync(mediaUri, body, credentials, ct);

    var result = response.Element(
      XmlHelpers.NsMedia + "GetVideoAnalyticsConfigurationsResponse");
    return result?.HasElements == true;
  }

  public async Task<bool> EnableAnalyticsOnMetadataAsync(
    string mediaUri, Credentials credentials, CancellationToken ct)
  {
    var listBody = new XElement(XmlHelpers.NsMedia + "GetMetadataConfigurations");
    XElement response;
    try
    {
      response = await soap.SendAsync(mediaUri, listBody, credentials, ct);
    }
    catch
    {
      return false;
    }

    var configs = response.Element(XmlHelpers.NsMedia + "GetMetadataConfigurationsResponse");
    if (configs == null) return false;

    foreach (var config in configs.Elements(XmlHelpers.NsMedia + "Configurations"))
    {
      var analyticsEl = config.Element(XmlHelpers.NsSchema + "Analytics");
      if (analyticsEl != null && string.Equals(analyticsEl.Value, "true", StringComparison.OrdinalIgnoreCase))
        continue;

      if (analyticsEl != null)
        analyticsEl.Value = "true";
      else
        config.Add(new XElement(XmlHelpers.NsSchema + "Analytics", "true"));

      var setBody = new XElement(XmlHelpers.NsMedia + "SetMetadataConfiguration",
        config,
        new XElement(XmlHelpers.NsMedia + "ForcePersistence", "true"));

      try
      {
        await soap.SendAsync(mediaUri, setBody, credentials, ct);
      }
      catch
      {
        return false;
      }
    }

    return true;
  }

  private static string? ParseCodec(string? encoding) => encoding?.ToUpperInvariant() switch
  {
    "H264" => "h264",
    "H265" or "HEVC" => "h265",
    "JPEG" => "mjpeg",
    "MPEG4" => "mpeg4",
    _ => encoding?.ToLowerInvariant()
  };

  private static bool HasMovablePtzConfiguration(XElement? ptzConfig)
  {
    if (ptzConfig == null) return false;
    var limits = ptzConfig.Element(XmlHelpers.NsSchema + "PanTiltLimits");
    if (limits == null) return true;
    var range = limits.Element(XmlHelpers.NsSchema + "Range");
    if (range == null) return true;
    return HasNonDegenerateRange(range.Element(XmlHelpers.NsSchema + "XRange"))
      && HasNonDegenerateRange(range.Element(XmlHelpers.NsSchema + "YRange"));
  }

  private static bool HasNonDegenerateRange(XElement? range)
  {
    if (range == null) return false;
    if (!double.TryParse(range.Element(XmlHelpers.NsSchema + "Min")?.Value, out var min)) return false;
    if (!double.TryParse(range.Element(XmlHelpers.NsSchema + "Max")?.Value, out var max)) return false;
    return max > min;
  }
}

public sealed class OnvifProfile
{
  public required string Token { get; init; }
  public string? Name { get; init; }
  public string? Codec { get; init; }
  public int? Width { get; init; }
  public int? Height { get; init; }
  public int? Fps { get; init; }
  public int? Bitrate { get; init; }
  public bool HasPtzBinding { get; init; }
}
