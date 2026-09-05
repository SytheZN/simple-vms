using System.Buffers.Binary;
using System.IO.Compression;
using System.Threading.Channels;
using Format.MotionGrid;
using Shared.Models.Formats;

namespace Tests.Unit.MotionGrid;

[TestFixture]
public class MotionGridFormatTests
{
  /// <summary>
  /// SCENARIO:
  /// A single sync MotionGridUnit is fed into the muxer
  ///
  /// ACTION:
  /// Mux one unit and inspect the produced fragment bytes
  ///
  /// EXPECTED RESULT:
  /// Fragment has a 22-byte header with MGRD magic, version 1, sync flag set, timestamp/width/height
  /// little-endian, 4-byte payload length, followed by a deflate-compressed delta that decompresses
  /// to the original cells (XOR with zeros for a sync frame)
  /// </summary>
  [Test]
  public async Task Mux_SingleSyncUnit_ProducesExpectedHeaderAndPayload()
  {
    var cells = new byte[8];
    for (var i = 0; i < cells.Length; i++)
      cells[i] = (byte)(i * 32);

    var input = new TestUnitStream();
    input.Emit(new MotionGridUnit
    {
      Data = cells,
      Timestamp = 0x1234_5678_9ABC_DEF0,
      IsSyncPoint = true,
      Width = 4,
      Height = 2
    });
    input.Complete();

    var muxer = new MotionGridMuxer(input, "mgrd");
    var fragments = new List<MotionGridFragment>();
    await foreach (var f in muxer.MuxAsync(CancellationToken.None))
      fragments.Add(f);

    Assert.That(fragments, Has.Count.EqualTo(1));
    var bytes = fragments[0].Data.ToArray();

    Assert.That(bytes[..4], Is.EqualTo("MGRD"u8.ToArray()));
    Assert.That(bytes[4], Is.EqualTo(1));
    Assert.That(bytes[5], Is.EqualTo(1)); // FlagSyncPoint
    Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(6)),
      Is.EqualTo(0x1234_5678_9ABC_DEF0UL));
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14)), Is.EqualTo(4));
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(16)), Is.EqualTo(2));

    var payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(18));
    Assert.That(bytes.Length, Is.EqualTo(22 + payloadLength));

    var delta = Decompress(bytes.AsSpan(22, payloadLength));
    Assert.That(delta, Is.EqualTo(cells));
  }

  /// <summary>
  /// SCENARIO:
  /// Two muxed fragments are written to a stream with leading noise
  ///
  /// ACTION:
  /// Segment reader scans for MGRD magic and reads both fragments back
  ///
  /// EXPECTED RESULT:
  /// Reader yields two fragments with correct timestamps and IsSyncPoint flags; decompressing
  /// each payload and XOR-accumulating recovers the original cell data
  /// </summary>
  [Test]
  public async Task SegmentReader_ResyncsOnMagicAndYieldsFragments()
  {
    var cells1 = new byte[] { 0x10, 0x20, 0x30, 0x40 };
    var cells2 = new byte[] { 0x50, 0x60, 0x70, 0x80 };

    var input = new TestUnitStream();
    input.Emit(new MotionGridUnit
    {
      Data = cells1,
      Timestamp = 1000,
      IsSyncPoint = true,
      Width = 2,
      Height = 2
    });
    input.Emit(new MotionGridUnit
    {
      Data = cells2,
      Timestamp = 2000,
      IsSyncPoint = false,
      Width = 2,
      Height = 2
    });
    input.Complete();

    var muxer = new MotionGridMuxer(input, "mgrd");
    using var ms = new MemoryStream();
    ms.Write(new byte[] { 0x4D, 0x47, 0x52, 0x00 }); // leading noise with partial magic
    await foreach (var f in muxer.MuxAsync(CancellationToken.None))
      ms.Write(f.Data.ToArray());
    ms.Position = 0;

    await using var reader = new MotionGridSegmentReader(ms);
    var fragments = new List<MotionGridFragment>();
    await foreach (var f in reader.ReadAsync(CancellationToken.None))
      fragments.Add((MotionGridFragment)f);

    Assert.That(fragments, Has.Count.EqualTo(2));

    Assert.That(fragments[0].Timestamp, Is.EqualTo(1000UL));
    Assert.That(fragments[0].IsSyncPoint, Is.True);
    var payload0 = ExtractPayload(fragments[0].Data);
    var delta0 = Decompress(payload0);
    var recovered0 = XorWith(delta0, new byte[delta0.Length]); // sync: XOR with zeros
    Assert.That(recovered0, Is.EqualTo(cells1));

    Assert.That(fragments[1].Timestamp, Is.EqualTo(2000UL));
    Assert.That(fragments[1].IsSyncPoint, Is.False);
    var payload1 = ExtractPayload(fragments[1].Data);
    var delta1 = Decompress(payload1);
    var recovered1 = XorWith(delta1, cells1); // non-sync: XOR with prev
    Assert.That(recovered1, Is.EqualTo(cells2));
  }

  private static ReadOnlySpan<byte> ExtractPayload(ReadOnlyMemory<byte> data)
  {
    var span = data.Span;
    var payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[18..]);
    return span.Slice(22, payloadLength);
  }

  private static byte[] Decompress(ReadOnlySpan<byte> compressed)
  {
    using var input = new MemoryStream(compressed.ToArray());
    using var deflate = new DeflateStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    deflate.CopyTo(output);
    return output.ToArray();
  }

  private static byte[] XorWith(byte[] data, byte[] prev)
  {
    var result = new byte[data.Length];
    for (var i = 0; i < data.Length; i++)
      result[i] = (byte)(data[i] ^ prev[i]);
    return result;
  }

  private sealed class TestUnitStream : IDataStream<MotionGridUnit>
  {
    private readonly Channel<MotionGridUnit> _channel = Channel.CreateUnbounded<MotionGridUnit>();

    public StreamInfo Info { get; } = new() { DataFormat = "motion-grid" };
    public Type FrameType => typeof(MotionGridUnit);

    public void Emit(MotionGridUnit unit) => _channel.Writer.TryWrite(unit);
    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<MotionGridUnit> ReadAsync(
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
