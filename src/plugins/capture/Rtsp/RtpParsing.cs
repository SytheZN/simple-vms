using Shared.Models.Formats;

namespace Capture.Rtsp;

internal static class RtpParsing
{
  internal static ReadOnlyMemory<byte> ExtractRtpPayload(ReadOnlyMemory<byte> rtpPacket)
  {
    var span = rtpPacket.Span;
    if (span.Length < 12)
      return ReadOnlyMemory<byte>.Empty;

    var cc = span[0] & 0x0F;
    var hasExtension = (span[0] & 0x10) != 0;
    var offset = 12 + cc * 4;

    if (hasExtension && offset + 4 <= span.Length)
    {
      var extensionLength = (span[offset + 2] << 8) | span[offset + 3];
      offset += 4 + extensionLength * 4;
    }

    return offset < rtpPacket.Length ? rtpPacket[offset..] : ReadOnlyMemory<byte>.Empty;
  }

  internal static ulong ExtractRtpTimestamp(ReadOnlyMemory<byte> rtpPacket)
  {
    var span = rtpPacket.Span;
    if (span.Length < 8)
      return 0;

    return ((ulong)span[4] << 24) | ((ulong)span[5] << 16) |
           ((ulong)span[6] << 8) | span[7];
  }

  internal static H264Parameters? BuildH264Parameters(SdpMediaDescription media)
  {
    if (!media.FormatParameters.TryGetValue("sprop-parameter-sets", out var spropSets))
      return null;

    var parts = spropSets.Split(',', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2)
      return null;

    return new H264Parameters
    {
      Sps = Convert.FromBase64String(parts[0]),
      Pps = Convert.FromBase64String(parts[1])
    };
  }

  internal static H265Parameters? BuildH265Parameters(SdpMediaDescription media)
  {
    var hasVps = media.FormatParameters.TryGetValue("sprop-vps", out var vpsB64);
    var hasSps = media.FormatParameters.TryGetValue("sprop-sps", out var spsB64);
    var hasPps = media.FormatParameters.TryGetValue("sprop-pps", out var ppsB64);

    if (!hasVps || !hasSps || !hasPps)
      return null;

    return new H265Parameters
    {
      Vps = Convert.FromBase64String(vpsB64!),
      Sps = Convert.FromBase64String(spsB64!),
      Pps = Convert.FromBase64String(ppsB64!)
    };
  }
}
