using Capture.Rtsp;

namespace Tests.Unit.Streaming;

[TestFixture]
public class SdpParserMetadataTests
{
  /// <summary>
  /// SCENARIO:
  /// SDP contains an m=application track with ONVIF metadata codec
  ///
  /// ACTION:
  /// Parse the SDP
  ///
  /// EXPECTED RESULT:
  /// The application track is included in results with correct MediaType and codec
  /// </summary>
  [Test]
  public void Parse_ApplicationTrack_Detected()
  {
    var sdp = """
      v=0
      o=- 0 0 IN IP4 0.0.0.0
      s=RTSP Session
      m=application 0 RTP/AVP 107
      a=rtpmap:107 vnd.onvif.metadata/90000
      a=control:trackID=3
      """;

    var results = SdpParser.Parse(sdp);

    Assert.That(results, Has.Count.EqualTo(1));
    Assert.That(results[0].MediaType, Is.EqualTo("application"));
    Assert.That(results[0].Codec, Is.EqualTo("VND.ONVIF.METADATA"));
    Assert.That(results[0].ControlUri, Is.EqualTo("trackID=3"));
  }

  /// <summary>
  /// SCENARIO:
  /// SDP contains both a video track and an application metadata track
  ///
  /// ACTION:
  /// Parse the SDP
  ///
  /// EXPECTED RESULT:
  /// Both tracks are returned
  /// </summary>
  [Test]
  public void Parse_VideoAndApplication_ReturnsBoth()
  {
    var sdp = """
      v=0
      o=- 0 0 IN IP4 0.0.0.0
      s=RTSP Session
      m=video 0 RTP/AVP 96
      a=rtpmap:96 H264/90000
      a=control:trackID=1
      m=application 0 RTP/AVP 107
      a=rtpmap:107 vnd.onvif.metadata/90000
      a=control:trackID=3
      """;

    var results = SdpParser.Parse(sdp);

    Assert.That(results, Has.Count.EqualTo(2));
    Assert.That(results[0].MediaType, Is.EqualTo("video"));
    Assert.That(results[0].Codec, Is.EqualTo("H264"));
    Assert.That(results[1].MediaType, Is.EqualTo("application"));
    Assert.That(results[1].Codec, Is.EqualTo("VND.ONVIF.METADATA"));
  }

  /// <summary>
  /// SCENARIO:
  /// SDP contains only an m=audio track (not video or application)
  ///
  /// ACTION:
  /// Parse the SDP
  ///
  /// EXPECTED RESULT:
  /// Empty list (audio tracks are not handled)
  /// </summary>
  [Test]
  public void Parse_AudioOnly_ReturnsEmpty()
  {
    var sdp = """
      v=0
      m=audio 0 RTP/AVP 0
      a=rtpmap:0 PCMU/8000
      a=control:trackID=2
      """;

    var results = SdpParser.Parse(sdp);

    Assert.That(results, Is.Empty);
  }
}
