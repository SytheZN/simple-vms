using System.Diagnostics;
using Analyzer.Thumbnail;

namespace ThumbnailBench;

/// <summary>
/// Accumulates phase time across a whole run. A clock read costs about as much as the smaller
/// phases it measures, so one block in <see cref="Interval"/> is timed and the totals are scaled
/// back up; a run covers millions of blocks, so the sample is far larger than the split needs to
/// settle.
///
/// What a reading costs is measured the same way everything else is - by timing an interval that
/// contains nothing, on the same blocks, between the same neighbours. A loop of back-to-back reads
/// would measure how fast they retire when the processor can overlap them, which is not what an
/// interval bracketing real work pays.
/// </summary>
internal sealed class PhaseRecorder : IReconstructionObserver
{
  /// <summary>Prime, so the sample cannot phase-lock onto a position in the quadtree.</summary>
  private const int Interval = 17;

  private const int Phases = 13;
  private static readonly int Baseline = (int)ReconstructionPhase.Baseline;

  private readonly long[] _elapsed = new long[Phases];
  private readonly long[] _intervals = new long[Phases];
  private readonly long[] _opened = new long[Phases];
  private bool _timing;

  public int Blocks { get; private set; }
  public int Coded { get; private set; }

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

  /// <summary>Ticks an interval costs to take, which each phase is charged once per interval.</summary>
  private double Overhead =>
    _intervals[Baseline] == 0 ? 0 : (double)_elapsed[Baseline] / _intervals[Baseline];

  public double OverheadNanoseconds => Overhead * 1_000_000_000 / Stopwatch.Frequency;

  public double Ms(ReconstructionPhase phase)
  {
    var charged = _elapsed[(int)phase] - _intervals[(int)phase] * Overhead;
    return charged * Interval * 1000 / Stopwatch.Frequency;
  }
}
