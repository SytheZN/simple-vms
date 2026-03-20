using Capture.Rtsp;

namespace Tests.Unit.Streaming;

[TestFixture]
public class SdpParserTests
{
  /// <summary>
  /// SCENARIO:
  /// SDP with a single H.264 video track including sprop-parameter-sets
  ///
  /// ACTION:
  /// Parse the SDP
  ///
  /// EXPECTED RESULT:
  /// Returns one media description with codec=H264 and extracted format parameters
  /// </summary>
  [Test]
  public void ParseH264Sdp_ExtractsCodecAndSprop()
  {
    var sdp = """
      v=0
      o=- 0 0 IN IP4 0.0.0.0
      s=RTSP Session
      m=video 0 RTP/AVP 96
      a=rtpmap:96 H264/90000
      a=fmtp:96 packetization-mode=1;sprop-parameter-sets=Z0IAKeKQFAe3,aM48gA==
      a=control:trackID=1
      """;

    var results = SdpParser.Parse(sdp);

    Assert.That(results, Has.Count.EqualTo(1));
    Assert.That(results[0].Codec, Is.EqualTo("H264"));
    Assert.That(results[0].ClockRate, Is.EqualTo(90000));
    Assert.That(results[0].ControlUri, Is.EqualTo("trackID=1"));
    Assert.That(results[0].FormatParameters["sprop-parameter-sets"],
      Is.EqualTo("Z0IAKeKQFAe3,aM48gA=="));
  }

  /// <summary>
  /// SCENARIO:
  /// SDP with a single H.265 video track including VPS/SPS/PPS
  ///
  /// ACTION:
  /// Parse the SDP
  ///
  /// EXPECTED RESULT:
  /// Returns one media description with codec=H265 and sprop-vps/sps/pps parameters
  /// </summary>
  [Test]
  public void ParseH265Sdp_ExtractsVpsSpsPps()
  {
    var sdp = """
      v=0
      o=- 0 0 IN IP4 0.0.0.0
      s=Session
      m=video 0 RTP/AVP 96
      a=rtpmap:96 H265/90000
      a=fmtp:96 sprop-vps=QAEMAf//AWAAAAMAAAMAAAMAAAMAlqwJ;sprop-sps=RAEHAA==;sprop-pps=RAHA
      a=control:track1
      """;

    var results = SdpParser.Parse(sdp);

    Assert.That(results, Has.Count.EqualTo(1));
    Assert.That(results[0].Codec, Is.EqualTo("H265"));
    Assert.That(results[0].FormatParameters.ContainsKey("sprop-vps"), Is.True);
    Assert.That(results[0].FormatParameters.ContainsKey("sprop-sps"), Is.True);
    Assert.That(results[0].FormatParameters.ContainsKey("sprop-pps"), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// SDP with multiple media lines (audio + video)
  ///
  /// ACTION:
  /// Parse the SDP
  ///
  /// EXPECTED RESULT:
  /// Returns only the video track(s)
  /// </summary>
  [Test]
  public void ParseMultiTrack_ReturnsOnlyVideoTracks()
  {
    var sdp = """
      v=0
      o=- 0 0 IN IP4 0.0.0.0
      s=Session
      m=audio 0 RTP/AVP 0
      a=rtpmap:0 PCMU/8000
      a=control:trackID=0
      m=video 0 RTP/AVP 96
      a=rtpmap:96 H264/90000
      a=fmtp:96 packetization-mode=1
      a=control:trackID=1
      m=video 0 RTP/AVP 97
      a=rtpmap:97 H265/90000
      a=control:trackID=2
      """;

    var results = SdpParser.Parse(sdp);

    Assert.That(results, Has.Count.EqualTo(2));
    Assert.That(results[0].Codec, Is.EqualTo("H264"));
    Assert.That(results[1].Codec, Is.EqualTo("H265"));
  }

  /// <summary>
  /// SCENARIO:
  /// SDP with no video tracks
  ///
  /// ACTION:
  /// Parse the SDP
  ///
  /// EXPECTED RESULT:
  /// Returns empty list
  /// </summary>
  [Test]
  public void ParseNoVideo_ReturnsEmpty()
  {
    var sdp = """
      v=0
      o=- 0 0 IN IP4 0.0.0.0
      s=Session
      m=audio 0 RTP/AVP 0
      a=rtpmap:0 PCMU/8000
      a=control:trackID=0
      """;

    var results = SdpParser.Parse(sdp);

    Assert.That(results, Has.Count.EqualTo(0));
  }
}
