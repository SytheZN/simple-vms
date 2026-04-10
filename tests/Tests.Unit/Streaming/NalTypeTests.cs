using Capture.Rtsp;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class NalTypeTests
{
  /// <summary>
  /// SCENARIO:
  /// H.264 NAL byte with type=5 (IDR)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H264NalType.Idr
  /// </summary>
  [Test]
  public void H264_Idr_ClassifiedCorrectly()
  {
    var result = RtpH264Depacketizer.ClassifyH264(0x65); // NRI=3, type=5
    Assert.That(result, Is.EqualTo(H264NalType.Idr));
  }

  /// <summary>
  /// SCENARIO:
  /// H.264 NAL byte with type=7 (SPS)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H264NalType.Sps
  /// </summary>
  [Test]
  public void H264_Sps_ClassifiedCorrectly()
  {
    var result = RtpH264Depacketizer.ClassifyH264(0x67); // NRI=3, type=7
    Assert.That(result, Is.EqualTo(H264NalType.Sps));
  }

  /// <summary>
  /// SCENARIO:
  /// H.264 NAL byte with type=8 (PPS)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H264NalType.Pps
  /// </summary>
  [Test]
  public void H264_Pps_ClassifiedCorrectly()
  {
    var result = RtpH264Depacketizer.ClassifyH264(0x68); // NRI=3, type=8
    Assert.That(result, Is.EqualTo(H264NalType.Pps));
  }

  /// <summary>
  /// SCENARIO:
  /// H.264 IDR NAL unit created via CreateH264NalUnit
  ///
  /// ACTION:
  /// Check IsSyncPoint
  ///
  /// EXPECTED RESULT:
  /// IsSyncPoint is true
  /// </summary>
  [Test]
  public void H264_IdrNalUnit_IsSyncPoint()
  {
    byte[] data = [0x65, 0xAA];
    var nal = RtpH264Depacketizer.CreateH264NalUnit(data, 1000);

    Assert.That(nal.IsSyncPoint, Is.True);
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Idr));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 NAL byte with type=19 (IDR_W_RADL)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H265NalType.IdrWRadl
  /// </summary>
  [Test]
  public void H265_IdrWRadl_ClassifiedCorrectly()
  {
    var result = RtpH265Depacketizer.ClassifyH265(0x26); // (19 << 1) = 0x26
    Assert.That(result, Is.EqualTo(H265NalType.IdrWRadl));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 NAL byte with type=20 (IDR_N_LP)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H265NalType.IdrNLp
  /// </summary>
  [Test]
  public void H265_IdrNLp_ClassifiedCorrectly()
  {
    var result = RtpH265Depacketizer.ClassifyH265(0x28); // (20 << 1) = 0x28
    Assert.That(result, Is.EqualTo(H265NalType.IdrNLp));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 NAL byte with type=32 (VPS)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H265NalType.Vps
  /// </summary>
  [Test]
  public void H265_Vps_ClassifiedCorrectly()
  {
    var result = RtpH265Depacketizer.ClassifyH265(0x40); // (32 << 1) = 0x40
    Assert.That(result, Is.EqualTo(H265NalType.Vps));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 NAL byte with type=33 (SPS)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H265NalType.Sps
  /// </summary>
  [Test]
  public void H265_Sps_ClassifiedCorrectly()
  {
    var result = RtpH265Depacketizer.ClassifyH265(0x42); // (33 << 1) = 0x42
    Assert.That(result, Is.EqualTo(H265NalType.Sps));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 NAL byte with type=34 (PPS)
  ///
  /// ACTION:
  /// Classify the NAL type
  ///
  /// EXPECTED RESULT:
  /// Returns H265NalType.Pps
  /// </summary>
  [Test]
  public void H265_Pps_ClassifiedCorrectly()
  {
    var result = RtpH265Depacketizer.ClassifyH265(0x44); // (34 << 1) = 0x44
    Assert.That(result, Is.EqualTo(H265NalType.Pps));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 IDR_W_RADL NAL unit created via CreateH265NalUnit
  ///
  /// ACTION:
  /// Check IsSyncPoint
  ///
  /// EXPECTED RESULT:
  /// IsSyncPoint is true
  /// </summary>
  [Test]
  public void H265_IdrNalUnit_IsSyncPoint()
  {
    byte[] data = [0x26, 0x01, 0xAA];
    var nal = RtpH265Depacketizer.CreateH265NalUnit(data, 1000);

    Assert.That(nal.IsSyncPoint, Is.True);
    Assert.That(nal.NalType, Is.EqualTo(H265NalType.IdrWRadl));
  }
}
