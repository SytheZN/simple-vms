namespace Client.Core.Controls;

public sealed class TripleTapTracker
{
  private readonly TimeSpan _window;
  private readonly Func<DateTimeOffset> _clock;
  private DateTimeOffset _firstTapAt;
  private int _count;

  public TripleTapTracker(TimeSpan? window = null, Func<DateTimeOffset>? clock = null)
  {
    _window = window ?? TimeSpan.FromMilliseconds(1500);
    _clock = clock ?? (() => DateTimeOffset.UtcNow);
  }

  public bool Record()
  {
    var now = _clock();
    if (_count == 0 || now - _firstTapAt > _window)
    {
      _firstTapAt = now;
      _count = 1;
      return false;
    }
    _count++;
    if (_count < 3) return false;
    _count = 0;
    return true;
  }
}
