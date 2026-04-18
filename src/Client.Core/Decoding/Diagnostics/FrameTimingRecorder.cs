using System.Diagnostics;

namespace Client.Core.Decoding.Diagnostics;

public sealed class FrameTimingRecorder
{
  public const int Capacity = 120;

  private readonly long[] _deltas = new long[Capacity];
  private long _lastTicks;
  private int _count;

  public int SampleCount => Math.Min(_count, Capacity);

  public void Record()
  {
    var now = Stopwatch.GetTimestamp();
    var last = _lastTicks;
    _lastTicks = now;
    if (last == 0) return;
    _deltas[_count % Capacity] = now - last;
    _count++;
  }

  public void CopyMs(Span<double> dest)
  {
    var samples = Math.Min(Math.Min(dest.Length, Capacity), _count);
    var freq = (double)Stopwatch.Frequency;
    var leading = dest.Length - samples;
    for (var i = 0; i < leading; i++) dest[i] = 0;
    for (var i = 0; i < samples; i++)
    {
      var srcIdx = ((_count - samples + i) % Capacity + Capacity) % Capacity;
      dest[leading + i] = _deltas[srcIdx] * 1000.0 / freq;
    }
  }
}
