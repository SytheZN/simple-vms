using System.Buffers.Binary;
using System.Threading.Channels;
using Format.Mjpeg;
using Shared.Models;
using Shared.Models.Formats;

namespace Tests.Unit.Mjpeg;

[TestFixture]
public class MjpegFormatTests
{
  /// <summary>
  /// SCENARIO:
  /// A single JpegUnit is fed into the muxer
  ///
  /// ACTION:
  /// Mux the unit and inspect the produced fragment bytes
  ///
  /// EXPECTED RESULT:
  /// Fragment has a 17-byte header with MJPG magic, version 1, little-endian timestamp and
  /// payload length, followed by the JPEG bytes unmodified
  /// </summary>
  [Test]
  public async Task Mux_SingleUnit_ProducesExpectedHeaderAndPayload()
  {
    var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0xFF, 0xD9 };

    var input = new TestUnitStream();
    input.Emit(new JpegUnit
    {
      Data = jpeg,
      Timestamp = 0x1234_5678_9ABC_DEF0,
      Width = 240,
      Height = 135
    });
    input.Complete();

    var fragments = await MuxAllAsync(input);

    Assert.That(fragments, Has.Count.EqualTo(1));
    var bytes = fragments[0].Data.ToArray();

    Assert.That(bytes[..4], Is.EqualTo("MJPG"u8.ToArray()));
    Assert.That(bytes[4], Is.EqualTo(1));
    Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(5)),
      Is.EqualTo(0x1234_5678_9ABC_DEF0UL));

    var payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(13));
    Assert.That(payloadLength, Is.EqualTo(jpeg.Length));
    Assert.That(bytes.Length, Is.EqualTo(17 + payloadLength));
    Assert.That(bytes.AsSpan(17).ToArray(), Is.EqualTo(jpeg));
  }

  /// <summary>
  /// SCENARIO:
  /// Init describes the stream without reading it, so no unit is consumed before muxing
  ///
  /// ACTION:
  /// Call Init, then enumerate MuxAsync over an input carrying three units
  ///
  /// EXPECTED RESULT:
  /// All three units are muxed, in order, with none swallowed by Init
  /// </summary>
  [Test]
  public async Task Mux_AfterInit_MuxesEveryUnit()
  {
    var input = new TestUnitStream();
    for (var i = 0; i < 3; i++)
      input.Emit(NewUnit(timestamp: (ulong)(1000 + i)));
    input.Complete();

    var muxer = new MjpegMuxer(input, "mjpg");
    muxer.Init();

    var fragments = new List<JpegFragment>();
    await foreach (var f in muxer.MuxAsync(CancellationToken.None))
      fragments.Add(f);

    Assert.That(fragments.Select(f => f.Timestamp), Is.EqualTo(new ulong[] { 1000, 1001, 1002 }));
  }

  /// <summary>
  /// SCENARIO:
  /// The muxer describes the stream from configuration alone
  ///
  /// ACTION:
  /// Call Init on a 4 fps input
  ///
  /// EXPECTED RESULT:
  /// MuxStreamInfo reports the mjpeg data format, image/jpeg mime type, the configured file
  /// extension and the input's fps, with resolution left unset because each JPEG carries its own
  /// </summary>
  [Test]
  public void Init_DescribesTheStream()
  {
    var input = new TestUnitStream(fps: 4m);

    var info = new MjpegMuxer(input, "mjpg").Init();

    Assert.Multiple(() =>
    {
      Assert.That(info.DataFormat, Is.EqualTo("mjpeg"));
      Assert.That(info.MimeType, Is.EqualTo("image/jpeg"));
      Assert.That(info.FileExtension, Is.EqualTo("mjpg"));
      Assert.That(info.Resolution, Is.Empty);
      Assert.That(info.Fps, Is.EqualTo(4));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Every JPEG is independently decodable, so every fragment is a sync point
  ///
  /// ACTION:
  /// Mux two units and inspect IsSyncPoint on each fragment
  ///
  /// EXPECTED RESULT:
  /// Both fragments report IsSyncPoint, so the segment writer indexes every frame
  /// </summary>
  [Test]
  public async Task Mux_EveryFragment_IsASyncPoint()
  {
    var input = new TestUnitStream();
    input.Emit(NewUnit(timestamp: 1));
    input.Emit(NewUnit(timestamp: 2));
    input.Complete();

    var fragments = await MuxAllAsync(input);

    Assert.That(fragments, Has.Count.EqualTo(2));
    Assert.That(fragments.All(f => f.IsSyncPoint), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// An analyzer that never produces a unit must not stall pipeline construction
  ///
  /// ACTION:
  /// Call Init on a stream that produces nothing, then mux it
  ///
  /// EXPECTED RESULT:
  /// Init returns usable stream info without waiting, and muxing yields no fragments
  /// </summary>
  [Test]
  public async Task Init_ProducerThatNeverEmits_DoesNotBlock()
  {
    var input = new TestUnitStream();
    input.Complete();

    var muxer = new MjpegMuxer(input, "mjpg");
    var info = muxer.Init();

    var fragments = new List<JpegFragment>();
    await foreach (var f in muxer.MuxAsync(CancellationToken.None))
      fragments.Add(f);

    Assert.That(info.MimeType, Is.EqualTo("image/jpeg"));
    Assert.That(fragments, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// The format plugin declares how it is discovered and what it converts
  ///
  /// ACTION:
  /// Inspect FormatId, FileExtension, InputType and OutputType
  ///
  /// EXPECTED RESULT:
  /// Format is identified as mjpeg with an mjpg extension, converting JpegUnit to JpegFragment,
  /// so StreamingService can match it to a derived stream declaring FormatId "mjpeg"
  /// </summary>
  [Test]
  public void StreamFormat_Describes_MjpegConversion()
  {
    var plugin = new MjpegPlugin();

    Assert.Multiple(() =>
    {
      Assert.That(plugin.FormatId, Is.EqualTo("mjpeg"));
      Assert.That(plugin.FileExtension, Is.EqualTo("mjpg"));
      Assert.That(plugin.InputType, Is.EqualTo(typeof(JpegUnit)));
      Assert.That(plugin.OutputType, Is.EqualTo(typeof(JpegFragment)));
      Assert.That(plugin.Metadata.Id, Is.EqualTo("mjpeg"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A pipeline is created over an input stream carrying two JPEG units
  ///
  /// ACTION:
  /// Call CreatePipelineAsync and enumerate the resulting mux stream
  ///
  /// EXPECTED RESULT:
  /// A mux stream is returned with an empty header - each JPEG is self-contained, so there is no
  /// init segment - reporting JpegFragment frames, and it yields both units in order
  /// </summary>
  [Test]
  public async Task CreatePipeline_OverInput_YieldsMuxStreamWithNoHeader()
  {
    var input = new TestUnitStream();
    input.Emit(NewUnit(timestamp: 10));
    input.Emit(NewUnit(timestamp: 20));
    input.Complete();

    var result = await new MjpegPlugin().CreatePipelineAsync(
      input, input.Info, CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    var muxStream = result.AsT0;

    Assert.Multiple(() =>
    {
      Assert.That(muxStream.Header.IsEmpty, Is.True);
      Assert.That(muxStream.FrameType, Is.EqualTo(typeof(JpegFragment)));
      Assert.That(muxStream.Info.MimeType, Is.EqualTo("image/jpeg"));
    });

    var timestamps = new List<ulong>();
    await foreach (var fragment in muxStream.ReadAsync(CancellationToken.None))
      timestamps.Add(fragment.Timestamp);

    Assert.That(timestamps, Is.EqualTo(new ulong[] { 10, 20 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Playback of recorded MJPEG segments is not implemented while the feature is live-only
  ///
  /// ACTION:
  /// Call CreateReader
  ///
  /// EXPECTED RESULT:
  /// An Unavailable error is returned rather than a reader, so callers propagate a clear failure
  /// instead of receiving a reader that cannot parse anything
  /// </summary>
  [Test]
  public void CreateReader_WhileLiveOnly_ReturnsUnavailable()
  {
    var result = new MjpegPlugin().CreateReader(new MemoryStream());

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }

  private static async Task<List<JpegFragment>> MuxAllAsync(TestUnitStream input)
  {
    var fragments = new List<JpegFragment>();
    await foreach (var f in new MjpegMuxer(input, "mjpg").MuxAsync(CancellationToken.None))
      fragments.Add(f);
    return fragments;
  }

  private static JpegUnit NewUnit(ulong timestamp = 1, ushort width = 240, ushort height = 135) =>
    new()
    {
      Data = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 },
      Timestamp = timestamp,
      Width = width,
      Height = height
    };

  private sealed class TestUnitStream : IDataStream<JpegUnit>
  {
    private readonly Channel<JpegUnit> _channel = Channel.CreateUnbounded<JpegUnit>();

    public StreamInfo Info { get; }
    public Type FrameType => typeof(JpegUnit);

    public TestUnitStream(decimal? fps = null)
    {
      Info = new StreamInfo { DataFormat = "mjpeg", Fps = fps };
    }

    public void Emit(JpegUnit unit) => _channel.Writer.TryWrite(unit);
    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<JpegUnit> ReadAsync(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
      while (await _channel.Reader.WaitToReadAsync(ct))
        while (_channel.Reader.TryRead(out var unit))
          yield return unit;
    }

    IAsyncEnumerable<IDataUnit> IDataStream.ReadAsync(CancellationToken ct) =>
      ReadAsDataUnits(ct);

    private async IAsyncEnumerable<IDataUnit> ReadAsDataUnits(
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
      await foreach (var unit in ReadAsync(ct))
        yield return unit;
    }
  }
}
