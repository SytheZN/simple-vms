using Capture.Rtsp;
using Shared.Models.Formats;

namespace Tests.Unit.Streaming;

[TestFixture]
public class RtpH264DepacketizerTests
{
  private RtpH264Depacketizer _depacketizer = null!;

  [SetUp]
  public void SetUp() => _depacketizer = new RtpH264Depacketizer();

  /// <summary>
  /// SCENARIO:
  /// RTP payload contains a single NAL unit (type 1-23)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns H264NalUnit with Annex B start code prepended
  /// </summary>
  [Test]
  public void SingleNal_ReturnsNalUnitWithStartCode()
  {
    byte[] payload = [0x65, 0x88, 0x04]; // IDR slice
    var result = _depacketizer.ProcessPacket(payload, 1000);

    Assert.That(result, Is.Not.Null);
    var nal = (H264NalUnit)result!;
    Assert.That(nal.Data.Span[0], Is.EqualTo(0x65));
    Assert.That(nal.Data.Length, Is.EqualTo(3));
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Idr));
    Assert.That(nal.IsSyncPoint, Is.True);
    Assert.That(nal.Timestamp, Is.EqualTo(1000));
  }

  /// <summary>
  /// SCENARIO:
  /// RTP payload is a STAP-A packet (type 24) containing two NAL units
  ///
  /// ACTION:
  /// Process via ProcessStapAAll
  ///
  /// EXPECTED RESULT:
  /// Returns both NAL units with Annex B start codes
  /// </summary>
  [Test]
  public void StapA_SplitsIntoMultipleNalUnits()
  {
    // STAP-A header (type 24 = 0x18), NRI=3 -> 0x78
    // NAL 1: SPS (0x67, 0x42) - 2 bytes, length prefix 0x0002
    // NAL 2: PPS (0x68, 0xCE) - 2 bytes, length prefix 0x0002
    byte[] payload = [0x78, 0x00, 0x02, 0x67, 0x42, 0x00, 0x02, 0x68, 0xCE];
    var results = _depacketizer.ProcessStapAAll(payload, 2000);

    Assert.That(results, Has.Count.EqualTo(2));

    var sps = (H264NalUnit)results[0];
    Assert.That(sps.NalType, Is.EqualTo(H264NalType.Sps));
    Assert.That(sps.Data.Span[0], Is.EqualTo(0x67));

    var pps = (H264NalUnit)results[1];
    Assert.That(pps.NalType, Is.EqualTo(H264NalType.Pps));
    Assert.That(pps.Data.Span[0], Is.EqualTo(0x68));
  }

  /// <summary>
  /// SCENARIO:
  /// Three FU-A packets arrive: start, middle, end
  ///
  /// ACTION:
  /// Process each packet sequentially
  ///
  /// EXPECTED RESULT:
  /// First two return null, third returns reassembled NAL unit
  /// </summary>
  [Test]
  public void FuA_ReassemblesFromStartMiddleEnd()
  {
    // FU indicator: NRI=3, type=28 -> 0x7C
    // FU header start: S=1, type=5(IDR) -> 0x85
    byte[] start = [0x7C, 0x85, 0xAA, 0xBB];
    var r1 = _depacketizer.ProcessPacket(start, 3000);
    Assert.That(r1, Is.Null);

    // FU header middle: S=0, E=0, type=5 -> 0x05
    byte[] middle = [0x7C, 0x05, 0xCC, 0xDD];
    var r2 = _depacketizer.ProcessPacket(middle, 3000);
    Assert.That(r2, Is.Null);

    // FU header end: E=1, type=5 -> 0x45
    byte[] end = [0x7C, 0x45, 0xEE, 0xFF];
    var r3 = _depacketizer.ProcessPacket(end, 3000);

    Assert.That(r3, Is.Not.Null);
    var nal = (H264NalUnit)r3!;
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Idr));
    Assert.That(nal.IsSyncPoint, Is.True);
    // Reconstructed header (1) + fragment data (6 bytes: AA BB CC DD EE FF)
    Assert.That(nal.Data.Length, Is.EqualTo(7));
  }

  /// <summary>
  /// SCENARIO:
  /// FU-A middle packet arrives without a preceding start packet
  ///
  /// ACTION:
  /// Process the middle packet
  ///
  /// EXPECTED RESULT:
  /// Returns null (incomplete fragment discarded)
  /// </summary>
  [Test]
  public void FuA_DroppedStart_ReturnsNull()
  {
    byte[] middle = [0x7C, 0x05, 0xCC, 0xDD];
    var result = _depacketizer.ProcessPacket(middle, 4000);

    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Non-IDR slice NAL unit (type 1)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// IsSyncPoint is false, NalType is Slice
  /// </summary>
  [Test]
  public void SingleNal_NonIdr_IsNotSyncPoint()
  {
    byte[] payload = [0x41, 0x9A]; // type=1 (slice), NRI=2
    var result = _depacketizer.ProcessPacket(payload, 5000);

    Assert.That(result, Is.Not.Null);
    var nal = (H264NalUnit)result!;
    Assert.That(nal.IsSyncPoint, Is.False);
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Slice));
  }

  /// <summary>
  /// SCENARIO:
  /// Empty RTP payload
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void EmptyPayload_ReturnsNull()
  {
    var result = _depacketizer.ProcessPacket(ReadOnlySpan<byte>.Empty, 0);
    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// SEI NAL unit (type 6)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// NalType is Sei, not a sync point
  /// </summary>
  [Test]
  public void SingleNal_Sei_ClassifiedCorrectly()
  {
    byte[] payload = [0x06, 0x05, 0x10];
    var result = _depacketizer.ProcessPacket(payload, 6000);

    Assert.That(result, Is.Not.Null);
    var nal = (H264NalUnit)result!;
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Sei));
    Assert.That(nal.IsSyncPoint, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Unknown NAL type (e.g. type 12)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// NalType is Other
  /// </summary>
  [Test]
  public void SingleNal_UnknownType_ClassifiedAsOther()
  {
    byte[] payload = [0x0C, 0xAA]; // type=12
    var result = _depacketizer.ProcessPacket(payload, 7000);

    Assert.That(result, Is.Not.Null);
    var nal = (H264NalUnit)result!;
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Other));
  }

  /// <summary>
  /// SCENARIO:
  /// STAP-A with truncated length field
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns empty list (graceful handling)
  /// </summary>
  [Test]
  public void StapA_TruncatedPayload_ReturnsEmpty()
  {
    byte[] payload = [0x78]; // STAP-A header only, no NAL data
    var results = _depacketizer.ProcessStapAAll(payload, 8000);

    Assert.That(results, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// FU-A with only 1 byte (missing FU header)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void FuA_TooShort_ReturnsNull()
  {
    byte[] payload = [0x7C]; // FU indicator only
    var result = _depacketizer.ProcessPacket(payload, 9000);

    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// NAL type 25-27 (reserved/unsupported)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void UnsupportedNalType_ReturnsNull()
  {
    byte[] payload = [0x79, 0xAA]; // type=25
    var result = _depacketizer.ProcessPacket(payload, 10000);

    Assert.That(result, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// SPS NAL unit (type 7)
  ///
  /// ACTION:
  /// Process the packet
  ///
  /// EXPECTED RESULT:
  /// NalType is Sps, not a sync point
  /// </summary>
  [Test]
  public void SingleNal_Sps_NotSyncPoint()
  {
    byte[] payload = [0x67, 0x42, 0x00, 0x1E]; // SPS
    var result = _depacketizer.ProcessPacket(payload, 11000);

    Assert.That(result, Is.Not.Null);
    var nal = (H264NalUnit)result!;
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Sps));
    Assert.That(nal.IsSyncPoint, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// PPS NAL unit (type 8)
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
    byte[] payload = [0x68, 0xCE, 0x38, 0x80]; // PPS
    var result = _depacketizer.ProcessPacket(payload, 12000);

    Assert.That(result, Is.Not.Null);
    var nal = (H264NalUnit)result!;
    Assert.That(nal.NalType, Is.EqualTo(H264NalType.Pps));
    Assert.That(nal.IsSyncPoint, Is.False);
  }
}
