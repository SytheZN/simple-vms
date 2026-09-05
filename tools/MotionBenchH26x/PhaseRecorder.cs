using System.Diagnostics;
using Utils;

namespace MotionBenchH26x;

internal sealed class PhaseRecorder : IObserverHarness<ReconstructionPhase>
{
  private const int Interval = 17;

  private const int Phases = 14;
  private static readonly int Baseline = (int)ReconstructionPhase.Baseline;

  private readonly long[] _elapsed = new long[Phases];
  private readonly long[] _intervals = new long[Phases];
  private readonly long[] _opened = new long[Phases];
  private bool _timing;

  public long Blocks { get; private set; }
  public long Coded { get; private set; }

  public void Reset()
  {
    Array.Clear(_elapsed);
    Array.Clear(_intervals);
    Blocks = 0;
    Coded = 0;
    _timing = false;
  }

  public void Block(bool coded)
  {
    Blocks++;
    if (coded) Coded++;

    _timing = Blocks % Interval == 0;
    if (!_timing) return;

    var opened = Stopwatch.GetTimestamp();
    _elapsed[Baseline] += Stopwatch.GetTimestamp() - opened;
    _intervals[Baseline]++;
  }

  public void Begin(ReconstructionPhase phase)
  {
    if (_timing) _opened[(int)phase] = Stopwatch.GetTimestamp();
  }

  public void End(ReconstructionPhase phase)
  {
    if (!_timing) return;

    _elapsed[(int)phase] += Stopwatch.GetTimestamp() - _opened[(int)phase];
    _intervals[(int)phase]++;
  }

  private double Overhead =>
    _intervals[Baseline] == 0 ? 0 : (double)_elapsed[Baseline] / _intervals[Baseline];

  public double OverheadNanoseconds => Overhead * 1_000_000_000 / Stopwatch.Frequency;

  public double Ms(ReconstructionPhase phase)
  {
    var charged = _elapsed[(int)phase] - _intervals[(int)phase] * Overhead;
    return charged * Interval * 1000 / Stopwatch.Frequency;
  }
}
