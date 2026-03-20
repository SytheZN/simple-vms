using System.Buffers.Binary;
using System.Text;
using Format.Fmp4;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class H265MuxerTests
{
  private static readonly byte[] TestVps =
  [
    0x40, 0x01, 0x0C, 0x01, 0x01, 0x60, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7B
  ];

  private static readonly byte[] TestSps =
  [
    0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7B, 0xA0,
    0x03, 0xC0, 0x80, 0x10, 0xE5, 0x80
  ];

  private static readonly byte[] TestPps = [0x44, 0x01, 0xC1, 0x72, 0xB4, 0x62, 0x40];

  /// <summary>
  /// SCENARIO:
  /// Feed H.265 NALs through the muxer
  ///
  /// ACTION:
  /// Provide VPS, SPS, PPS, IDR, slices and collect output
  ///
  /// EXPECTED RESULT:
  /// First fragment is init segment, followed by data fragments
  /// </summary>
  [Test]
  public async Task H265Mux_ProducesInitThenFragments()
  {
    var nals = CreateH265NalSequence(2);
    var input = new TestDataStream<H265NalUnit>(nals);

    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H265, input, timestamps);

    var fragments = new List<Fmp4Fragment>();
    await foreach (var fragment in muxer.MuxAsync(CancellationToken.None))
      fragments.Add(fragment);

    Assert.That(fragments.Count, Is.GreaterThanOrEqualTo(3));
    Assert.That(fragments[0].IsHeader, Is.True);
    Assert.That(fragments[1].IsHeader, Is.False);
    Assert.That(fragments[1].IsSyncPoint, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 init segment contains hev1 sample entry
  ///
  /// ACTION:
  /// Mux H.265 NALs, check init segment for hev1 box
  ///
  /// EXPECTED RESULT:
  /// Init segment contains "hev1" sample entry and "hvcC" config box
  /// </summary>
  [Test]
  public async Task H265Mux_InitSegmentContainsHev1()
  {
    var nals = CreateH265NalSequence(1);
    var input = new TestDataStream<H265NalUnit>(nals);

    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H265, input, timestamps);

    var fragments = new List<Fmp4Fragment>();
    await foreach (var fragment in muxer.MuxAsync(CancellationToken.None))
      fragments.Add(fragment);

    var init = fragments[0].Data.ToArray();
    Assert.That(FindBox(init, "hev1"), Is.GreaterThan(-1));
    Assert.That(FindBox(init, "hvcC"), Is.GreaterThan(-1));
  }

  /// <summary>
  /// SCENARIO:
  /// H.265 output has valid box structure
  ///
  /// ACTION:
  /// Mux NALs, parse top-level boxes
  ///
  /// EXPECTED RESULT:
  /// ftyp, moov, then alternating moof+mdat pairs
  /// </summary>
  [Test]
  public async Task H265Mux_OutputHasValidBoxStructure()
  {
    var nals = CreateH265NalSequence(2);
    var input = new TestDataStream<H265NalUnit>(nals);

    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H265, input, timestamps);

    var allData = new MemoryStream();
    await foreach (var fragment in muxer.MuxAsync(CancellationToken.None))
      allData.Write(fragment.Data.Span);

    var output = allData.ToArray();
    var boxTypes = ParseTopLevelBoxTypes(output);

    Assert.That(boxTypes[0], Is.EqualTo("ftyp"));
    Assert.That(boxTypes[1], Is.EqualTo("moov"));
    for (var i = 2; i < boxTypes.Count; i += 2)
    {
      Assert.That(boxTypes[i], Is.EqualTo("moof"), $"Box {i}");
      Assert.That(boxTypes[i + 1], Is.EqualTo("mdat"), $"Box {i + 1}");
    }
  }

  private static List<H265NalUnit> CreateH265NalSequence(int gopCount)
  {
    var nals = new List<H265NalUnit>();
    ulong ts = 0;

    for (var gop = 0; gop < gopCount; gop++)
    {
      nals.Add(MakeNal(TestVps, ts, H265NalType.Vps));
      nals.Add(MakeNal(TestSps, ts, H265NalType.Sps));
      nals.Add(MakeNal(TestPps, ts, H265NalType.Pps));
      nals.Add(MakeIdr(ts));
      ts += 33333;
      for (var i = 0; i < 29; i++)
      {
        nals.Add(MakeTrail(ts));
        ts += 33333;
      }
    }

    return nals;
  }

  private static H265NalUnit MakeNal(byte[] data, ulong ts, H265NalType type) => new()
  {
    Data = Prepend(data),
    Timestamp = ts,
    IsSyncPoint = false,
    NalType = type
  };

  private static H265NalUnit MakeIdr(ulong ts) => new()
  {
    Data = Prepend([0x26, 0x01, 0xAF, 0x08, 0x44, 0x00, 0xFF, 0xBB]),
    Timestamp = ts,
    IsSyncPoint = true,
    NalType = H265NalType.IdrWRadl
  };

  private static H265NalUnit MakeTrail(ulong ts) => new()
  {
    Data = Prepend([0x02, 0x01, 0xD0, 0x33, 0x24, 0xFF]),
    Timestamp = ts,
    IsSyncPoint = false,
    NalType = H265NalType.TrailR
  };

  private static byte[] Prepend(byte[] nal)
  {
    var result = new byte[4 + nal.Length];
    result[3] = 1;
    nal.CopyTo(result, 4);
    return result;
  }

  private static int FindBox(byte[] data, string type)
  {
    var target = Encoding.ASCII.GetBytes(type);
    for (var i = 0; i <= data.Length - 8; i++)
    {
      if (data[i + 4] == target[0] && data[i + 5] == target[1]
        && data[i + 6] == target[2] && data[i + 7] == target[3])
        return i;
    }
    return -1;
  }

  private static List<string> ParseTopLevelBoxTypes(byte[] data)
  {
    var types = new List<string>();
    var offset = 0;
    while (offset + 8 <= data.Length)
    {
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
      if (size < 8) break;
      types.Add(Encoding.ASCII.GetString(data, offset + 4, 4));
      offset += size;
    }
    return types;
  }
}
