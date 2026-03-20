using System.Buffers.Binary;
using System.Text;
using System.Threading.Channels;
using Format.Fmp4;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Fmp4;

[TestFixture]
public class MuxerRoundTripTests
{
  // Minimal valid H.264 Baseline SPS: 1920x1080
  private static readonly byte[] TestSps =
  [
    0x67, 0x42, 0x00, 0x2A, 0x96, 0x35, 0x40, 0xF0,
    0x04, 0x4F, 0xCB, 0x37, 0x01, 0x01, 0x01, 0x40,
    0x00, 0x01, 0xC2, 0x00, 0x00, 0x57, 0xE4, 0x01
  ];

  private static readonly byte[] TestPps = [0x68, 0xCE, 0x38, 0x80];

  /// <summary>
  /// SCENARIO:
  /// Feed a sequence of H.264 NALs through the muxer
  ///
  /// ACTION:
  /// Provide SPS, PPS, IDR, slice, IDR, slice and collect output fragments
  ///
  /// EXPECTED RESULT:
  /// First fragment is IsHeader=true (init segment), followed by data fragments
  /// with correct IsSyncPoint flags
  /// </summary>
  [Test]
  public async Task H264Mux_ProducesInitThenFragments()
  {
    var nals = CreateH264NalSequence(2);
    var input = new TestDataStream<H264NalUnit>(nals);

    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H264, input, timestamps);

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
  /// Init segment contains ftyp + moov
  ///
  /// ACTION:
  /// Mux H.264 NALs, take the first (header) fragment
  ///
  /// EXPECTED RESULT:
  /// Data starts with ftyp box, followed by moov box
  /// </summary>
  [Test]
  public async Task H264Mux_InitSegmentHasFtypAndMoov()
  {
    var nals = CreateH264NalSequence(1);
    var input = new TestDataStream<H264NalUnit>(nals);

    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H264, input, timestamps);

    var fragments = new List<Fmp4Fragment>();
    await foreach (var fragment in muxer.MuxAsync(CancellationToken.None))
      fragments.Add(fragment);

    var init = fragments[0].Data.Span;
    var ftypType = Encoding.ASCII.GetString(init.Slice(4, 4));
    var ftypSize = BinaryPrimitives.ReadUInt32BigEndian(init);
    var moovType = Encoding.ASCII.GetString(init.Slice((int)ftypSize + 4, 4));

    Assert.That(ftypType, Is.EqualTo("ftyp"));
    Assert.That(moovType, Is.EqualTo("moov"));
  }

  /// <summary>
  /// SCENARIO:
  /// Keyframe offsets are reported via callback
  ///
  /// ACTION:
  /// Mux 3 GOPs (keyframe + slices each), collect keyframe offsets
  ///
  /// EXPECTED RESULT:
  /// 3 keyframe offsets reported, each pointing to a moof box
  /// </summary>
  [Test]
  public async Task H264Mux_KeyframeOffsetsPointToMoofBoxes()
  {
    var nals = CreateH264NalSequence(3);
    var input = new TestDataStream<H264NalUnit>(nals);

    var keyframes = new List<KeyframeOffset>();
    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H264, input, timestamps, kf => keyframes.Add(kf));

    var allData = new MemoryStream();
    await foreach (var fragment in muxer.MuxAsync(CancellationToken.None))
      allData.Write(fragment.Data.Span);

    var output = allData.ToArray();

    Assert.That(keyframes.Count, Is.EqualTo(3));
    foreach (var kf in keyframes)
    {
      var boxType = Encoding.ASCII.GetString(output, (int)kf.ByteOffset + 4, 4);
      Assert.That(boxType, Is.EqualTo("moof"), $"Offset {kf.ByteOffset} should be a moof box");
    }
  }

  /// <summary>
  /// SCENARIO:
  /// Segment reader can seek to a keyframe offset and read fragments
  ///
  /// ACTION:
  /// Mux NALs, write to stream, seek to second keyframe offset, read fragments
  ///
  /// EXPECTED RESULT:
  /// First fragment read after seek is a keyframe
  /// </summary>
  [Test]
  public async Task SegmentReader_SeeksToKeyframeAndReadsFragments()
  {
    var nals = CreateH264NalSequence(3);
    var input = new TestDataStream<H264NalUnit>(nals);

    var keyframes = new List<KeyframeOffset>();
    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H264, input, timestamps, kf => keyframes.Add(kf));

    var allData = new MemoryStream();
    await foreach (var fragment in muxer.MuxAsync(CancellationToken.None))
      allData.Write(fragment.Data.Span);

    Assert.That(keyframes.Count, Is.GreaterThanOrEqualTo(2));

    var stream = new MemoryStream(allData.ToArray());
    var reader = new Fmp4SegmentReader(stream);
    await reader.SeekAsync(keyframes[1].ByteOffset, CancellationToken.None);

    var readFragments = new List<IDataUnit>();
    await foreach (var frag in reader.ReadAsync(CancellationToken.None))
      readFragments.Add(frag);

    Assert.That(readFragments.Count, Is.GreaterThan(0));
    Assert.That(readFragments[0].IsSyncPoint, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Concatenated init + fragments form a valid fMP4 structure
  ///
  /// ACTION:
  /// Mux NALs, concatenate all output, parse top-level boxes
  ///
  /// EXPECTED RESULT:
  /// Box sequence: ftyp, moov, moof, mdat, moof, mdat, ...
  /// </summary>
  [Test]
  public async Task H264Mux_OutputHasValidBoxStructure()
  {
    var nals = CreateH264NalSequence(2);
    var input = new TestDataStream<H264NalUnit>(nals);

    var timestamps = new TimestampConverter(90000);
    var muxer = new Fmp4Muxer(MuxerCodec.H264, input, timestamps);

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

  private static List<H264NalUnit> CreateH264NalSequence(int gopCount)
  {
    var nals = new List<H264NalUnit>();
    ulong ts = 0;

    for (var gop = 0; gop < gopCount; gop++)
    {
      nals.Add(MakeSps(ts));
      nals.Add(MakePps(ts));
      nals.Add(MakeIdr(ts));
      ts += 33333;
      for (var i = 0; i < 29; i++)
      {
        nals.Add(MakeSlice(ts));
        ts += 33333;
      }
    }

    return nals;
  }

  private static H264NalUnit MakeSps(ulong ts) => new()
  {
    Data = Prepend(TestSps),
    Timestamp = ts,
    IsSyncPoint = false,
    NalType = H264NalType.Sps
  };

  private static H264NalUnit MakePps(ulong ts) => new()
  {
    Data = Prepend(TestPps),
    Timestamp = ts,
    IsSyncPoint = false,
    NalType = H264NalType.Pps
  };

  private static H264NalUnit MakeIdr(ulong ts) => new()
  {
    Data = Prepend([0x65, 0x88, 0x04, 0x00, 0xFF, 0xAA, 0xBB, 0xCC]),
    Timestamp = ts,
    IsSyncPoint = true,
    NalType = H264NalType.Idr
  };

  private static H264NalUnit MakeSlice(ulong ts) => new()
  {
    Data = Prepend([0x41, 0x9A, 0x24, 0x6C, 0x41, 0xFF]),
    Timestamp = ts,
    IsSyncPoint = false,
    NalType = H264NalType.Slice
  };

  private static byte[] Prepend(byte[] nal)
  {
    var result = new byte[4 + nal.Length];
    result[3] = 1;
    nal.CopyTo(result, 4);
    return result;
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

internal sealed class TestDataStream<T> : IDataStream<T>, IDataStream where T : IDataUnit
{
  private readonly IReadOnlyList<T> _items;

  public TestDataStream(IReadOnlyList<T> items) => _items = items;

  public StreamInfo Info => new()
  {
    DataFormat = "h264",
    FormatParameters = null,
    Resolution = "1920x1080",
    Fps = 30
  };

  public Type FrameType => typeof(T);

  public async IAsyncEnumerable<T> ReadAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
    foreach (var item in _items)
    {
      yield return item;
      await Task.CompletedTask;
    }
  }
}
