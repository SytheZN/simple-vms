using Capture.Rtsp;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class RtpH265DepacketizerTests
{
  private RtpH265Depacketizer _depacketizer = null!;

  [SetUp]
  public void SetUp() => _depacketizer = new RtpH265Depacketizer();

  /// <summary>
  /// SCENARIO:
  /// RTP payload contains a single H.265 NAL unit (type 0-47)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns H265NalUnit with Annex B start code prepended
  /// </summary>
  [Test]
  public void SingleNal_ReturnsNalUnitWithStartCode()
  {
    // TRAIL_R (type=1): header byte0 = (1 << 1) = 0x02, byte1 = 0x01 (TID=1)
    byte[] payload = [0x02, 0x01, 0xAA, 0xBB];
    var result = _depacketizer.ProcessPacket(payload, 1000);

    Assert.That(result, Is.Not.Null);
    var nal = (H265NalUnit)result!;
    Assert.That(nal.Data.Span[0], Is.EqualTo(0x02));
    Assert.That(nal.Data.Length, Is.EqualTo(4));
    Assert.That(nal.NalType, Is.EqualTo(H265NalType.TrailR));
    Assert.That(nal.IsSyncPoint, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// RTP payload contains an IDR_W_RADL NAL unit (type=19)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// IsSyncPoint is true
  /// </summary>
  [Test]
  public void SingleNal_Idr_IsSyncPoint()
  {
    // IDR_W_RADL (type=19): header byte0 = (19 << 1) = 0x26, byte1 = 0x01
    byte[] payload = [0x26, 0x01, 0xCC];
    var result = _depacketizer.ProcessPacket(payload, 2000);

    Assert.That(result, Is.Not.Null);
    var nal = (H265NalUnit)result!;
    Assert.That(nal.IsSyncPoint, Is.True);
    Assert.That(nal.NalType, Is.EqualTo(H265NalType.IdrWRadl));
  }

  /// <summary>
  /// SCENARIO:
  /// RTP payload is an AP packet (type 48) containing two NAL units
  ///
  /// ACTION:
  /// Process via ProcessApAll
  ///
  /// EXPECTED RESULT:
  /// Returns both NAL units
  /// </summary>
  [Test]
  public void Ap_SplitsIntoMultipleNalUnits()
  {
    // AP header: type=48, byte0 = (48 << 1) = 0x60, byte1 = 0x01
    // NAL 1: VPS (type=32, byte0 = 0x40, byte1 = 0x01) - 2 bytes
    // NAL 2: SPS (type=33, byte0 = 0x42, byte1 = 0x01) - 2 bytes
    byte[] payload = [0x60, 0x01, 0x00, 0x02, 0x40, 0x01, 0x00, 0x02, 0x42, 0x01];
    var results = _depacketizer.ProcessApAll(payload, 3000);

    Assert.That(results, Has.Count.EqualTo(2));

    var vps = (H265NalUnit)results[0];
    Assert.That(vps.NalType, Is.EqualTo(H265NalType.Vps));

    var sps = (H265NalUnit)results[1];
    Assert.That(sps.NalType, Is.EqualTo(H265NalType.Sps));
  }

  /// <summary>
  /// SCENARIO:
  /// Three FU packets arrive: start, middle, end
  ///
  /// ACTION:
  /// Process each packet sequentially
  ///
  /// EXPECTED RESULT:
  /// First two return null, third returns reassembled NAL unit
  /// </summary>
  [Test]
  public void Fu_ReassemblesFromStartMiddleEnd()
  {
    // FU header: type=49, byte0 = (49 << 1) = 0x62, byte1 = 0x01
    // FU payload header start: S=1, type=19(IDR_W_RADL) -> 0x93
    byte[] start = [0x62, 0x01, 0x93, 0xAA];
    var r1 = _depacketizer.ProcessPacket(start, 4000);
    Assert.That(r1, Is.Null);

    // FU payload header middle: S=0, E=0, type=19 -> 0x13
    byte[] middle = [0x62, 0x01, 0x13, 0xBB];
    var r2 = _depacketizer.ProcessPacket(middle, 4000);
    Assert.That(r2, Is.Null);

    // FU payload header end: E=1, type=19 -> 0x53
    byte[] end = [0x62, 0x01, 0x53, 0xCC];
    var r3 = _depacketizer.ProcessPacket(end, 4000);

    Assert.That(r3, Is.Not.Null);
    var nal = (H265NalUnit)r3!;
    Assert.That(nal.IsSyncPoint, Is.True);
    Assert.That(nal.NalType, Is.EqualTo(H265NalType.IdrWRadl));
  }

  /// <summary>
  /// SCENARIO:
  /// Payload too short (less than 2 bytes)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void TooShortPayload_ReturnsNull()
  {
    byte[] payload = [0x02];
    var result = _depacketizer.ProcessPacket(payload, 0);
    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// IDR_N_LP NAL unit (type=20)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// IsSyncPoint is true, NalType is IdrNLp
  /// </summary>
  [Test]
  public void SingleNal_IdrNLp_IsSyncPoint()
  {
    // IDR_N_LP (type=20): byte0 = (20 << 1) = 0x28, byte1 = 0x01
    byte[] payload = [0x28, 0x01, 0xDD];
    var result = _depacketizer.ProcessPacket(payload, 5000);

    Assert.That(result, Is.Not.Null);
    var nal = (H265NalUnit)result!;
    Assert.That(nal.IsSyncPoint, Is.True);
    Assert.That(nal.NalType, Is.EqualTo(H265NalType.IdrNLp));
  }

  /// <summary>
  /// SCENARIO:
  /// PPS NAL unit (type=34)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// NalType is Pps, not a sync point
  /// </summary>
  [Test]
  public void SingleNal_Pps_NotSyncPoint()
  {
    // PPS (type=34): byte0 = (34 << 1) = 0x44, byte1 = 0x01
    byte[] payload = [0x44, 0x01, 0xBB];
    var result = _depacketizer.ProcessPacket(payload, 6000);

    Assert.That(result, Is.Not.Null);
    var nal = (H265NalUnit)result!;
    Assert.That(nal.NalType, Is.EqualTo(H265NalType.Pps));
    Assert.That(nal.IsSyncPoint, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// FU packet with missing start (middle arrives first)
  ///
  /// ACTION:
  /// Process middle then end
  ///
  /// EXPECTED RESULT:
  /// Both return null (incomplete fragment discarded)
  /// </summary>
  [Test]
  public void Fu_MissingStart_ReturnsNull()
  {
    byte[] middle = [0x62, 0x01, 0x13, 0xBB];
    var r1 = _depacketizer.ProcessPacket(middle, 7000);
    Assert.That(r1, Is.Null);

    byte[] end = [0x62, 0x01, 0x53, 0xCC];
    var r2 = _depacketizer.ProcessPacket(end, 7000);
    Assert.That(r2, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// FU packet too short (only 2 bytes, missing FU header)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void Fu_TooShort_ReturnsNull()
  {
    byte[] payload = [0x62, 0x01]; // FU header bytes but no FU payload header
    var result = _depacketizer.ProcessPacket(payload, 8000);
    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// SEI NAL unit (type=39)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// NalType is Sei
  /// </summary>
  [Test]
  public void SingleNal_Sei_ClassifiedCorrectly()
  {
    // SEI (type=39): byte0 = (39 << 1) = 0x4E, byte1 = 0x01
    byte[] payload = [0x4E, 0x01, 0xAA];
    var result = _depacketizer.ProcessPacket(payload, 9000);

    Assert.That(result, Is.Not.Null);
    var nal = (H265NalUnit)result!;
    Assert.That(nal.NalType, Is.EqualTo(H265NalType.Sei));
  }

  /// <summary>
  /// SCENARIO:
  /// AP packet with truncated payload (header only)
  ///
  /// ACTION:
  /// Process via ProcessApAll
  ///
  /// EXPECTED RESULT:
  /// Returns empty list
  /// </summary>
  [Test]
  public void Ap_TruncatedPayload_ReturnsEmpty()
  {
    byte[] payload = [0x60, 0x01]; // AP header only
    var results = _depacketizer.ProcessApAll(payload, 10000);

    Assert.That(results, Is.Empty);
  }
}
