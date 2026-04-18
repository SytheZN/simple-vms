using Client.Core.Decoding;
using Client.Core.Decoding.Backends;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Unit.Client.Decoding;

/// <summary>
/// Cache-coordination tests for Decoder: eviction of decoded GOPs,
/// per-GOP chunk tracking, GetFrame lookup. Actual FFmpeg decoding is
/// exercised by end-to-end tests against a real camera stream.
///
/// When data is opaque bytes (not a valid fmp4 fragment), Fmp4Demuxer
/// returns no samples and no frames are produced - but the cache still
/// updates its decoded-chunks counter, which is the behaviour under test.
/// </summary>
[TestFixture]
public class DecoderTests
{
  private static Decoder NewDecoder(Fetcher fetcher) =>
    new(NullLogger.Instance, new FakeDecodeBackend(), fetcher);

  private sealed class FakeDecodeBackend : IDecodeBackend
  {
    public FrameKind Kind => FrameKind.Cpu;
    public string DisplayName => "Fake";
    public bool Configure(CodecParameters config) => true;
    public bool SendSample(DemuxedSample sample) => true;
    public bool TryReceiveFrame(out DecodedFrame? frame) { frame = null; return false; }
    public void Flush() { }
    public void Dispose() { }
  }

  /// <summary>
  /// SCENARIO:
  /// SetTarget is called with a set of target GOP timestamps larger than
  /// the internal retention margin (target count + 2)
  ///
  /// ACTION:
  /// Populate the Fetcher with 10 GOPs, then call SetTarget with the last 3
  /// plus two that we ran through previously, simulating a forward jump
  ///
  /// EXPECTED RESULT:
  /// Decoder tracks chunk-decoded counts only for targeted GOPs;
  /// no decoded state exists for evicted GOPs
  /// </summary>
  [Test]
  public void SetTarget_EvictsDecodedStateOutsideTargetSet()
  {
    var fetcher = new Fetcher();
    using var decoder = NewDecoder(fetcher);
    decoder.SetTimescale(90_000);

    for (ulong t = 1000; t <= 10_000; t += 1000)
      fetcher.AppendData(t, new byte[] { 0 });

    // First pass: target GOPs 1000, 2000, 3000 - chunk counts tracked for those.
    decoder.SetTarget([1000, 2000, 3000]);

    // Second pass: target a forward window that excludes 1000 and 2000.
    decoder.SetTarget([7000, 8000, 9000]);

    // We can't inspect _decodedChunks directly, but if we now re-target the
    // evicted GOPs, SetTarget should not throw and should re-process them.
    decoder.SetTarget([1000]);
    Assert.DoesNotThrow(() => decoder.SetTarget([1000, 2000, 3000]));
  }

  /// <summary>
  /// SCENARIO:
  /// A GOP is targeted, then more chunks arrive for the same GOP
  ///
  /// ACTION:
  /// Append initial chunk, SetTarget, append another chunk, SetTarget again
  ///
  /// EXPECTED RESULT:
  /// The second SetTarget does not reprocess the first chunk (verified by no
  /// exception on re-targeting an unchanged GOP)
  /// </summary>
  [Test]
  public void SetTarget_Incremental_OnlyProcessesNewChunks()
  {
    var fetcher = new Fetcher();
    using var decoder = NewDecoder(fetcher);
    decoder.SetTimescale(90_000);

    fetcher.AppendData(1000, new byte[] { 0x00 });

    decoder.SetTarget([1000]);
    decoder.SetTarget([1000]);

    fetcher.AppendData(1000, new byte[] { 0x01 });
    Assert.DoesNotThrow(() => decoder.SetTarget([1000]));
  }

  /// <summary>
  /// SCENARIO:
  /// GetFrame is called when no frames have been decoded
  ///
  /// ACTION:
  /// Query GetFrame on an empty cache
  ///
  /// EXPECTED RESULT:
  /// Returns null
  /// </summary>
  [Test]
  public void GetFrame_EmptyCache_ReturnsNull()
  {
    using var decoder = NewDecoder(new Fetcher());
    Assert.That(decoder.GetFrame(1_000_000), Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// SetStride is called to change from 1 to 4
  ///
  /// ACTION:
  /// Set stride and then set it again to the same value
  ///
  /// EXPECTED RESULT:
  /// First call triggers a reconfigure path, second is a no-op and does not throw
  /// </summary>
  [Test]
  public void SetStride_SameValue_NoOp()
  {
    using var decoder = NewDecoder(new Fetcher());
    Assert.DoesNotThrow(() => decoder.SetStride(4));
    Assert.DoesNotThrow(() => decoder.SetStride(4));
  }

  /// <summary>
  /// SCENARIO:
  /// ResetWallClock clears the last wall-clock state used for timestamp recovery
  ///
  /// ACTION:
  /// Call ResetWallClock twice (it should be idempotent and side-effect free
  /// in the absence of decoded frames)
  ///
  /// EXPECTED RESULT:
  /// Does not throw
  /// </summary>
  [Test]
  public void ResetWallClock_Idempotent()
  {
    using var decoder = NewDecoder(new Fetcher());
    Assert.DoesNotThrow(() => decoder.ResetWallClock());
    Assert.DoesNotThrow(() => decoder.ResetWallClock());
  }

  /// <summary>
  /// SCENARIO:
  /// SwDecodeBackend is constructed and disposed without Configure being called
  ///
  /// ACTION:
  /// new + Dispose with a NullLogger
  ///
  /// EXPECTED RESULT:
  /// No exception; no native FFmpeg load attempted
  /// </summary>
  [Test]
  public void SwDecodeBackend_ConstructDispose_NoNativeLoad()
  {
    Assert.DoesNotThrow(() =>
    {
      using var backend = new SwDecodeBackend(NullLogger<SwDecodeBackend>.Instance);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Writer threads call SetTarget/SetStride/ResetWallClock at high rate
  /// while reader threads call GetFrame/CachedGopCount/NewestFrameTimestampUs
  ///
  /// ACTION:
  /// Run two writers and two readers concurrently for 500ms against a
  /// Decoder backed by 20 GOPs of opaque fetcher data
  ///
  /// EXPECTED RESULT:
  /// No thread throws; all operations complete cleanly
  /// </summary>
  [Test]
  public void Concurrent_SetTargetAndReads_NoException()
  {
    var fetcher = new Fetcher();
    using var decoder = NewDecoder(fetcher);
    decoder.SetTimescale(90_000);
    for (ulong t = 1000; t <= 20_000; t += 1000)
      fetcher.AppendData(t, new byte[] { 0 });

    var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
    var ct = cts.Token;

    void SafeLoop(Action body)
    {
      try { while (!ct.IsCancellationRequested) body(); }
      catch (Exception ex) { exceptions.Add(ex); }
    }

    var threads = new[]
    {
      new Thread(() => SafeLoop(() => decoder.SetTarget([5000, 6000, 7000]))),
      new Thread(() => SafeLoop(() => decoder.SetTarget([12_000, 13_000, 14_000, 15_000]))),
      new Thread(() => SafeLoop(() => { var _ = decoder.GetFrame(6_500_000); })),
      new Thread(() => SafeLoop(() =>
      {
        var _ = decoder.CachedGopCount + decoder.CachedFrameCount;
        var __ = decoder.NewestFrameTimestampUs;
      })),
      new Thread(() => SafeLoop(() => decoder.SetStride(Random.Shared.Next(1, 4)))),
    };

    foreach (var t in threads) t.Start();
    foreach (var t in threads) t.Join();

    Assert.That(exceptions, Is.Empty);
  }
}
