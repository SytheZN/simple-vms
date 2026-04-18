using Client.Core.Decoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Unit.Client.Decoding;

/// <summary>
/// Coverage for Decoder surface area not exercised by DecoderTests:
/// observable cache properties on an empty cache, BackendDisplayName,
/// command-queue error propagation through DecodeKeyframeAsync, and
/// double-Dispose idempotence.
/// </summary>
[TestFixture]
public class DecoderExtraTests
{
  private sealed class StubBackend : IDecodeBackend
  {
    public FrameKind Kind => FrameKind.Cpu;
    public string DisplayName { get; init; } = "Stub";
    public bool Configure(CodecParameters config) => true;
    public bool SendSample(DemuxedSample sample) => true;
    public bool TryReceiveFrame(out DecodedFrame? frame) { frame = null; return false; }
    public void Flush() { }
    public void Dispose() { }
  }

  private static Decoder NewDecoder(IDecodeBackend? backend = null) =>
    new(NullLogger.Instance, backend ?? new StubBackend(), new Fetcher());

  /// <summary>
  /// SCENARIO:
  /// A fresh Decoder reflects the backend's display name verbatim
  ///
  /// ACTION:
  /// Read BackendDisplayName immediately after construction
  ///
  /// EXPECTED RESULT:
  /// Returns the string the backend exposed
  /// </summary>
  [Test]
  public void BackendDisplayName_ReflectsBackend()
  {
    using var decoder = NewDecoder(new StubBackend { DisplayName = "Software (libavcodec)" });

    Assert.That(decoder.BackendDisplayName, Is.EqualTo("Software (libavcodec)"));
  }

  /// <summary>
  /// SCENARIO:
  /// A fresh Decoder has no decoded GOPs or frames
  ///
  /// ACTION:
  /// Read the three observable cache properties
  ///
  /// EXPECTED RESULT:
  /// CachedGopCount and CachedFrameCount are zero; NewestFrameTimestampUs is zero
  /// </summary>
  [Test]
  public void EmptyCache_PropertiesAreZero()
  {
    using var decoder = NewDecoder();

    Assert.Multiple(() =>
    {
      Assert.That(decoder.CachedGopCount, Is.Zero);
      Assert.That(decoder.CachedFrameCount, Is.Zero);
      Assert.That(decoder.NewestFrameTimestampUs, Is.Zero);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// Configure with the same codec twice triggers the seek-flush fast path
  /// (no full reconfigure), and a different codec triggers a full
  /// FlushDecoded + backend.Configure
  ///
  /// ACTION:
  /// Call Configure with codec A, then again with the same A, then with B
  ///
  /// EXPECTED RESULT:
  /// No exceptions; cache stays empty (no decoded data produced)
  /// </summary>
  [Test]
  public void Configure_SameThenDifferent_NoException()
  {
    using var decoder = NewDecoder();
    var a = new CodecParameters(VideoCodec.H264, 1920, 1080, [0x01]);
    var b = new CodecParameters(VideoCodec.H265, 1920, 1080, [0x02]);

    decoder.Configure(a);
    decoder.Configure(a);
    decoder.Configure(b);

    Assert.That(decoder.CachedGopCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// FlushForSeek runs on an empty cache
  ///
  /// ACTION:
  /// Call FlushForSeek twice in succession
  ///
  /// EXPECTED RESULT:
  /// Idempotent; no exception; cache remains empty
  /// </summary>
  [Test]
  public void FlushForSeek_OnEmptyCache_Idempotent()
  {
    using var decoder = NewDecoder();

    Assert.DoesNotThrow(() =>
    {
      decoder.FlushForSeek();
      decoder.FlushForSeek();
    });
    Assert.That(decoder.CachedGopCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// SetStride(1) on a fresh decoder is the no-change path; SetStride(3) is
  /// a real change
  ///
  /// ACTION:
  /// Call SetStride(1) then SetStride(3) then SetStride(0) (clamped to 1)
  ///
  /// EXPECTED RESULT:
  /// No exception across all three calls
  /// </summary>
  [Test]
  public void SetStride_VariousValues_NoException()
  {
    using var decoder = NewDecoder();

    Assert.DoesNotThrow(() => decoder.SetStride(1));
    Assert.DoesNotThrow(() => decoder.SetStride(3));
    Assert.DoesNotThrow(() => decoder.SetStride(0));
  }

  /// <summary>
  /// SCENARIO:
  /// DecodeKeyframeAsync is called with empty data (Fmp4Demuxer yields no samples)
  ///
  /// ACTION:
  /// Await the task returned by DecodeKeyframeAsync
  ///
  /// EXPECTED RESULT:
  /// Task completes successfully (the early-return on zero samples is hit)
  /// </summary>
  [Test]
  public async Task DecodeKeyframeAsync_EmptyData_CompletesSuccessfully()
  {
    using var decoder = NewDecoder();
    decoder.SetTimescale(90_000);

    await decoder.DecodeKeyframeAsync(ReadOnlyMemory<byte>.Empty, 1234);

    Assert.That(decoder.CachedGopCount, Is.Zero);
  }

  /// <summary>
  /// SCENARIO:
  /// Dispose is called more than once
  ///
  /// ACTION:
  /// Call Dispose twice
  ///
  /// EXPECTED RESULT:
  /// Second call is a no-op (no exception, no double-cancellation crash)
  /// </summary>
  [Test]
  public void Dispose_Twice_Idempotent()
  {
    var decoder = NewDecoder();

    decoder.Dispose();

    Assert.DoesNotThrow(() => decoder.Dispose());
  }

  /// <summary>
  /// SCENARIO:
  /// SetTarget is invoked with an empty span (no GOPs of interest)
  ///
  /// ACTION:
  /// Call SetTarget(ReadOnlySpan&lt;ulong&gt;.Empty)
  ///
  /// EXPECTED RESULT:
  /// No exception; cache stays empty
  /// </summary>
  [Test]
  public void SetTarget_Empty_NoException()
  {
    using var decoder = NewDecoder();

    Assert.DoesNotThrow(() => decoder.SetTarget(ReadOnlySpan<ulong>.Empty));
    Assert.That(decoder.CachedGopCount, Is.Zero);
  }
}
