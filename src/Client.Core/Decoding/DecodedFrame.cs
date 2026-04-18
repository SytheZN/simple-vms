using SimpleVms.FFmpeg.Native;

namespace Client.Core.Decoding;

/// <summary>
/// Refcounted; anything that takes a frame out of the cache must IncrementRef
/// and Dispose to match. OnReleased runs when the last ref drops.
/// </summary>
public abstract class DecodedFrame : IDisposable
{
  private int _refCount = 1;

  public long TimestampUs { get; }
  public long DurationUs { get; }
  public int Width { get; }
  public int Height { get; }
  public abstract FrameKind Kind { get; }

  protected DecodedFrame(long timestampUs, long durationUs, int width, int height)
  {
    TimestampUs = timestampUs;
    DurationUs = durationUs;
    Width = width;
    Height = height;
  }

  public void IncrementRef() => Interlocked.Increment(ref _refCount);

  public void Dispose()
  {
    if (Interlocked.Decrement(ref _refCount) != 0) return;
    OnReleased();
  }

  protected abstract void OnReleased();
}

public sealed unsafe class CpuDecodedFrame : DecodedFrame
{
  private nint _pixels;

  public override FrameKind Kind => FrameKind.Cpu;
  public nint Pixels => _pixels;
  public int Stride { get; }

  public CpuDecodedFrame(long timestampUs, long durationUs, nint pixels,
    int width, int height, int stride)
    : base(timestampUs, durationUs, width, height)
  {
    _pixels = pixels;
    Stride = stride;
  }

  protected override void OnReleased()
  {
    var p = Interlocked.Exchange(ref _pixels, 0);
    if (p != 0) FFAvUtil.av_free((void*)p);
  }
}
