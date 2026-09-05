namespace Shared.Models;

public sealed class MuxStreamStatsMonitor
{
  public static readonly TimeSpan BootstrapWindow = TimeSpan.FromSeconds(30);
  public static readonly TimeSpan SteadyWindow = TimeSpan.FromMinutes(5);

  private readonly Action<MuxStreamStats> _emit;
  private readonly Func<DateTimeOffset> _now;

  private DateTimeOffset _windowStart;
  private int _frameCount;
  private long _byteCount;
  private string _resolution = "";
  private bool _bootstrapped;

  public MuxStreamStatsMonitor(Action<MuxStreamStats> emit, Func<DateTimeOffset>? now = null)
  {
    _emit = emit;
    _now = now ?? (() => DateTimeOffset.UtcNow);
    _windowStart = _now();
  }

  public void RecordFrame(string frameResolution, int frameBytes)
  {
    if (!string.IsNullOrEmpty(frameResolution))
      _resolution = frameResolution;
    _frameCount++;
    _byteCount += frameBytes;

    var elapsed = _now() - _windowStart;
    var window = _bootstrapped ? SteadyWindow : BootstrapWindow;
    if (elapsed < window) return;

    var seconds = elapsed.TotalSeconds;
    _emit(new MuxStreamStats
    {
      Fps = seconds > 0
        ? Math.Round((decimal)(_frameCount / seconds), 2)
        : 0m,
      Resolution = _resolution,
      BitrateKbps = seconds > 0
        ? (int)Math.Round(_byteCount * 8 / seconds / 1000)
        : 0
    });

    _bootstrapped = true;
    _frameCount = 0;
    _byteCount = 0;
    _windowStart = _now();
  }
}
