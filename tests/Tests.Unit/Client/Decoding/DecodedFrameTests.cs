using Client.Core.Decoding;

namespace Tests.Unit.Client.Decoding;

[TestFixture]
public class DecodedFrameTests
{
  private sealed class FakeFrame(long ts, long dur, int w, int h) : DecodedFrame(ts, dur, w, h)
  {
    public int ReleaseCount;
    public override FrameKind Kind => FrameKind.Cpu;
    protected override void OnReleased() => Interlocked.Increment(ref ReleaseCount);
  }

  /// <summary>
  /// SCENARIO:
  /// A fresh DecodedFrame is created with a starting refcount of 1
  ///
  /// ACTION:
  /// Dispose once
  ///
  /// EXPECTED RESULT:
  /// OnReleased fires exactly once
  /// </summary>
  [Test]
  public void Dispose_FromFreshFrame_ReleasesOnce()
  {
    var f = new FakeFrame(100, 33, 1920, 1080);

    f.Dispose();

    Assert.That(f.ReleaseCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// IncrementRef raises the refcount before Dispose drops it
  ///
  /// ACTION:
  /// IncrementRef once, Dispose twice
  ///
  /// EXPECTED RESULT:
  /// OnReleased fires exactly once after the second Dispose
  /// </summary>
  [Test]
  public void IncrementRef_DelaysRelease()
  {
    var f = new FakeFrame(100, 33, 1920, 1080);
    f.IncrementRef();

    f.Dispose();
    Assert.That(f.ReleaseCount, Is.EqualTo(0));

    f.Dispose();
    Assert.That(f.ReleaseCount, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// Constructor stores the timing and dimension fields verbatim
  ///
  /// ACTION:
  /// Read TimestampUs, DurationUs, Width, Height after construction
  ///
  /// EXPECTED RESULT:
  /// All four match the constructor arguments
  /// </summary>
  [Test]
  public void Properties_RoundTrip()
  {
    using var f = new FakeFrame(123_456_789L, 33_333L, 1280, 720);

    Assert.Multiple(() =>
    {
      Assert.That(f.TimestampUs, Is.EqualTo(123_456_789L));
      Assert.That(f.DurationUs, Is.EqualTo(33_333L));
      Assert.That(f.Width, Is.EqualTo(1280));
      Assert.That(f.Height, Is.EqualTo(720));
      Assert.That(f.Kind, Is.EqualTo(FrameKind.Cpu));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// CpuDecodedFrame is created with a zero pixel pointer (no buffer to free)
  ///
  /// ACTION:
  /// Dispose
  ///
  /// EXPECTED RESULT:
  /// No native call is made (av_free is gated on non-zero pointer); no exception
  /// </summary>
  [Test]
  public void CpuDecodedFrame_ZeroPixels_DisposeIsNoFree()
  {
    var f = new CpuDecodedFrame(1000, 33, nint.Zero, 640, 480, 2560);

    Assert.That(f.Kind, Is.EqualTo(FrameKind.Cpu));
    Assert.That(f.Pixels, Is.EqualTo(nint.Zero));
    Assert.That(f.Stride, Is.EqualTo(2560));
    Assert.DoesNotThrow(() => f.Dispose());
  }

  /// <summary>
  /// SCENARIO:
  /// Many threads concurrently IncrementRef and Dispose the same frame
  ///
  /// ACTION:
  /// Spin up 8 threads each performing 1000 increment/dispose pairs, then a
  /// final Dispose to drop the original ref
  ///
  /// EXPECTED RESULT:
  /// OnReleased fires exactly once
  /// </summary>
  [Test]
  public void Refcount_ConcurrentIncrementDispose_ReleasesOnce()
  {
    var f = new FakeFrame(1, 1, 1, 1);

    var threads = new Thread[8];
    for (var i = 0; i < threads.Length; i++)
    {
      threads[i] = new Thread(() =>
      {
        for (var j = 0; j < 1000; j++)
        {
          f.IncrementRef();
          f.Dispose();
        }
      });
    }
    foreach (var t in threads) t.Start();
    foreach (var t in threads) t.Join();

    f.Dispose();

    Assert.That(f.ReleaseCount, Is.EqualTo(1));
  }
}
