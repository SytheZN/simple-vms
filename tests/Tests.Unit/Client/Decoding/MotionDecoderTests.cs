using System.Buffers.Binary;
using System.IO.Compression;
using Client.Core.Decoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class MotionDecoderTests
{
  private const ulong GopTs = 1_000_000;

  /// <summary>
  /// SCENARIO:
  /// A sync MGRD unit arrives in a fetched GOP chunk
  ///
  /// ACTION:
  /// Append the chunk, SetTarget on the GOP, GetFrame at the unit timestamp
  ///
  /// EXPECTED RESULT:
  /// The frame carries the decompressed cells, grid size, and sync flag
  /// </summary>
  [Test]
  public void Decode_SyncUnit_ProducesFrame()
  {
    var (fetcher, decoder) = NewDecoder();
    var cells = new byte[] { 0, 10, 20, 30, 40, 50 };
    fetcher.AppendData(GopTs, BuildUnit(GopTs, 3, 2, cells, sync: true), true);

    decoder.SetTarget([GopTs]);
    var frame = decoder.GetFrame((long)GopTs);

    Assert.That(frame, Is.Not.Null);
    Assert.Multiple(() =>
    {
      Assert.That(frame!.Cells, Is.EqualTo(cells));
      Assert.That(frame.Cols, Is.EqualTo(3));
      Assert.That(frame.Rows, Is.EqualTo(2));
      Assert.That(frame.Sync, Is.True);
      Assert.That(frame.TimestampUs, Is.EqualTo((long)GopTs));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// A delta unit follows a sync unit within the same GOP chain
  ///
  /// ACTION:
  /// Decode a chunk containing the sync unit for cellsA and a delta unit whose
  /// payload is cellsA XOR cellsB
  ///
  /// EXPECTED RESULT:
  /// The delta frame resolves to cellsB
  /// </summary>
  [Test]
  public void Decode_DeltaUnit_XorsAgainstChain()
  {
    var (fetcher, decoder) = NewDecoder();
    var cellsA = new byte[] { 1, 2, 3, 4 };
    var cellsB = new byte[] { 5, 6, 7, 8 };
    var delta = new byte[4];
    for (var i = 0; i < 4; i++) delta[i] = (byte)(cellsA[i] ^ cellsB[i]);

    var chunk = Concat(
      BuildUnit(GopTs, 2, 2, cellsA, sync: true),
      BuildUnit(GopTs + 100_000, 2, 2, delta, sync: false));
    fetcher.AppendData(GopTs, chunk, true);

    decoder.SetTarget([GopTs]);
    var frame = decoder.GetFrame((long)GopTs + 100_000);

    Assert.That(frame!.Cells, Is.EqualTo(cellsB));
  }

  /// <summary>
  /// SCENARIO:
  /// A chunk carries several concatenated MGRD units (the live path flushes
  /// every 10th frame, so multi-unit chunks are the norm)
  ///
  /// ACTION:
  /// Decode a chunk with three units
  ///
  /// EXPECTED RESULT:
  /// All three frames are decoded into the GOP
  /// </summary>
  [Test]
  public void Decode_ConcatenatedUnits_DecodesAll()
  {
    var (fetcher, decoder) = NewDecoder();
    var chunk = Concat(
      BuildUnit(GopTs, 2, 1, [1, 2], sync: true),
      BuildUnit(GopTs + 100_000, 2, 1, [0, 0], sync: false),
      BuildUnit(GopTs + 200_000, 2, 1, [3, 3], sync: false));
    fetcher.AppendData(GopTs, chunk, true);

    decoder.SetTarget([GopTs]);

    Assert.That(decoder.Stats().Frames, Is.EqualTo(3));
  }

  /// <summary>
  /// SCENARIO:
  /// A Begin chunk replaces a GOP that already had more chunks decoded
  /// (idempotent redelivery after an overlap resend)
  ///
  /// ACTION:
  /// Decode two chunks, replace with a single Begin chunk, SetTarget again,
  /// then append a continuation chunk and SetTarget once more
  ///
  /// EXPECTED RESULT:
  /// The decoded-chunk count clamps to the shortened list instead of
  /// re-decoding or throwing, and the continuation chunk decodes normally
  /// </summary>
  [Test]
  public void SetTarget_BeginReplaceShrinksChunks_ClampsAndContinues()
  {
    var (fetcher, decoder) = NewDecoder();
    fetcher.AppendData(GopTs, BuildUnit(GopTs, 2, 1, [1, 1], sync: true), true);
    fetcher.AppendData(GopTs, BuildUnit(GopTs + 100_000, 2, 1, [0, 1], sync: false), false);
    decoder.SetTarget([GopTs]);
    Assert.That(decoder.Stats().Frames, Is.EqualTo(2));

    fetcher.AppendData(GopTs, BuildUnit(GopTs, 2, 1, [1, 1], sync: true), true);
    decoder.SetTarget([GopTs]);
    Assert.That(decoder.Stats().Frames, Is.EqualTo(2));

    fetcher.AppendData(GopTs, BuildUnit(GopTs + 100_000, 2, 1, [0, 1], sync: false), false);
    decoder.SetTarget([GopTs]);
    Assert.That(decoder.Stats().Frames, Is.EqualTo(3));
  }

  /// <summary>
  /// SCENARIO:
  /// More GOPs are decoded than the retention cap for the current target set
  ///
  /// ACTION:
  /// Decode six GOPs, then SetTarget with only the newest
  ///
  /// EXPECTED RESULT:
  /// Non-target GOPs are evicted down to targets + 2
  /// </summary>
  [Test]
  public void SetTarget_ExcessGops_EvictsToCap()
  {
    var (fetcher, decoder) = NewDecoder();
    var all = new ulong[6];
    for (var i = 0; i < 6; i++)
    {
      all[i] = GopTs + (ulong)i * 4_000_000;
      fetcher.AppendData(all[i], BuildUnit(all[i], 2, 1, [1, 2], sync: true), true);
    }
    decoder.SetTarget(all);
    Assert.That(decoder.Stats().Gops, Is.EqualTo(6));

    decoder.SetTarget([all[5]]);

    Assert.That(decoder.Stats().Gops, Is.EqualTo(3));
  }

  /// <summary>
  /// SCENARIO:
  /// A chunk contains garbage instead of an MGRD unit
  ///
  /// ACTION:
  /// Decode a chunk without the MGRD magic
  ///
  /// EXPECTED RESULT:
  /// No frames are produced and no exception is thrown
  /// </summary>
  [Test]
  public void Decode_GarbageChunk_ProducesNothing()
  {
    var (fetcher, decoder) = NewDecoder();
    fetcher.AppendData(GopTs, new byte[] { 1, 2, 3, 4, 5 }, true);

    decoder.SetTarget([GopTs]);

    Assert.That(decoder.Stats().Frames, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// Flush is called after decoding
  ///
  /// ACTION:
  /// Decode a GOP, Flush, GetFrame
  ///
  /// EXPECTED RESULT:
  /// No frames remain
  /// </summary>
  [Test]
  public void Flush_ClearsDecodedState()
  {
    var (fetcher, decoder) = NewDecoder();
    fetcher.AppendData(GopTs, BuildUnit(GopTs, 2, 1, [1, 2], sync: true), true);
    decoder.SetTarget([GopTs]);

    decoder.Flush();

    Assert.That(decoder.GetFrame((long)GopTs), Is.Null);
  }

  private static (Fetcher Fetcher, MotionDecoder Decoder) NewDecoder()
  {
    var fetcher = new Fetcher();
    return (fetcher, new MotionDecoder(NullLogger.Instance, fetcher));
  }

  private static byte[] BuildUnit(ulong timestamp, ushort cols, ushort rows, byte[] cells, bool sync)
  {
    using var compressedStream = new MemoryStream();
    using (var deflate = new DeflateStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
      deflate.Write(cells);
    var compressed = compressedStream.ToArray();

    var unit = new byte[22 + compressed.Length];
    unit[0] = (byte)'M';
    unit[1] = (byte)'G';
    unit[2] = (byte)'R';
    unit[3] = (byte)'D';
    unit[4] = 1;
    unit[5] = (byte)(sync ? 1 : 0);
    BinaryPrimitives.WriteUInt64LittleEndian(unit.AsSpan(6), timestamp);
    BinaryPrimitives.WriteUInt16LittleEndian(unit.AsSpan(14), cols);
    BinaryPrimitives.WriteUInt16LittleEndian(unit.AsSpan(16), rows);
    BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(18), (uint)compressed.Length);
    compressed.CopyTo(unit, 22);
    return unit;
  }

  private static byte[] Concat(params byte[][] units)
  {
    var total = units.Sum(u => u.Length);
    var result = new byte[total];
    var offset = 0;
    foreach (var unit in units)
    {
      unit.CopyTo(result, offset);
      offset += unit.Length;
    }
    return result;
  }
}
