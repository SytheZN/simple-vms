namespace Capture.Rtsp;

public sealed class SdpMediaDescription
{
  public required string MediaType { get; init; }
  public required string Codec { get; init; }
  public required int ClockRate { get; init; }
  public required string ControlUri { get; init; }
  public Dictionary<string, string> FormatParameters { get; init; } = [];
}

public static class SdpParser
{
  public static IReadOnlyList<SdpMediaDescription> Parse(string sdp)
  {
    var results = new List<SdpMediaDescription>();
    string? currentMedia = null;
    int payloadType = -1;
    string? controlUri = null;
    var fmtpParams = new Dictionary<string, string>();
    string? codec = null;
    int clockRate = 90000;

    foreach (var rawLine in sdp.Split('\n'))
    {
      var line = rawLine.TrimEnd('\r');

      if (line.StartsWith("m="))
      {
        if (currentMedia != null && codec != null && controlUri != null)
          results.Add(Build(currentMedia, codec, clockRate, controlUri, fmtpParams));

        currentMedia = null;
        codec = null;
        clockRate = 90000;
        controlUri = null;
        payloadType = -1;
        fmtpParams = [];

        var parts = line[2..].Split(' ');
        if (parts.Length >= 4 && (parts[0] == "video" || parts[0] == "application"))
        {
          currentMedia = parts[0];
          if (int.TryParse(parts[3], out var pt))
            payloadType = pt;
        }
      }
      else if (currentMedia != null)
      {
        if (line.StartsWith("a=rtpmap:") && payloadType >= 0)
        {
          var rtpmap = line[$"a=rtpmap:{payloadType} ".Length..];
          var slashIdx = rtpmap.IndexOf('/');
          if (slashIdx > 0)
          {
            codec = rtpmap[..slashIdx].ToUpperInvariant();
            if (int.TryParse(rtpmap.AsSpan(slashIdx + 1).TrimEnd('/'), out var cr))
              clockRate = cr;
          }
          else
          {
            codec = rtpmap.ToUpperInvariant();
          }
        }
        else if (line.StartsWith($"a=fmtp:{payloadType} "))
        {
          var fmtp = line[$"a=fmtp:{payloadType} ".Length..];
          foreach (var param in fmtp.Split(';', StringSplitOptions.TrimEntries))
          {
            var eqIdx = param.IndexOf('=');
            if (eqIdx > 0)
              fmtpParams[param[..eqIdx].Trim()] = param[(eqIdx + 1)..].Trim();
          }
        }
        else if (line.StartsWith("a=control:"))
        {
          controlUri = line["a=control:".Length..].Trim();
        }
      }
    }

    if (currentMedia != null && codec != null && controlUri != null)
      results.Add(Build(currentMedia, codec, clockRate, controlUri, fmtpParams));

    return results;
  }

  private static SdpMediaDescription Build(
    string mediaType, string codec, int clockRate, string controlUri,
    Dictionary<string, string> fmtpParams)
  {
    return new SdpMediaDescription
    {
      MediaType = mediaType,
      Codec = codec,
      ClockRate = clockRate,
      ControlUri = controlUri,
      FormatParameters = fmtpParams
    };
  }
}
