using System.Diagnostics.CodeAnalysis;
using Analyzer.MotionGridH26x.Filters;
using Microsoft.Extensions.Logging;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x;

public readonly record struct ProcessorSettings(
  string Algorithm, int WindowFrames, bool Deblock, bool Despeckle);

public sealed class MotionGridProcessor
{
  private readonly Func<ProcessorSettings?> _poll;
  private readonly ILogger _logger;

  private IMotionGridAlgorithm _algorithm;
  private string _activeAlgorithm;
  private int _activeWindowFrames;
  private IFilter[] _filters;
  private bool _activeDeblock;
  private bool _activeDespeckle;

  public MotionGridProcessor(ProcessorSettings initial, Func<ProcessorSettings?> poll, ILogger logger)
  {
    _poll = poll;
    _logger = logger;

    _activeAlgorithm = initial.Algorithm;
    _activeWindowFrames = initial.WindowFrames;
    _algorithm = BuildAlgorithm(_activeAlgorithm, _activeWindowFrames);

    _activeDeblock = initial.Deblock;
    _activeDespeckle = initial.Despeckle;
    _filters = BuildFilters(_activeDeblock, _activeDespeckle);

    logger.LogDebug("Motion grid processor: filters=[{Filters}] algorithm={Algorithm}",
      string.Join(", ", _filters.Select(f => f.GetType().Name)), _algorithm.GetType().Name);
  }

  public void Feed(MotionGridUnit unit)
  {
    var settings = _poll();
    if (settings.HasValue)
      Reconcile(settings.Value);

    if (_filters.Length == 0)
    {
      _algorithm.Feed(unit);
      return;
    }

    _filters[0].Feed(unit);
    DrainFilters();
  }

  public bool TryReceive([MaybeNullWhen(false)] out MotionGridUnit unit) =>
    _algorithm.TryReceive(out unit);

  public void Flush()
  {
    FlushChain();
    _algorithm.Flush();
  }

  private void Reconcile(ProcessorSettings s)
  {
    var filtersChanged = s.Deblock != _activeDeblock || s.Despeckle != _activeDespeckle;
    var algorithmChanged = s.Algorithm != _activeAlgorithm || s.WindowFrames != _activeWindowFrames;

    if (!filtersChanged && !algorithmChanged)
      return;

    FlushChain();

    if (algorithmChanged)
    {
      _algorithm.Flush();
      while (_algorithm.TryReceive(out _)) { }
      _activeAlgorithm = s.Algorithm;
      _activeWindowFrames = s.WindowFrames;
      _algorithm = BuildAlgorithm(s.Algorithm, s.WindowFrames);
    }

    if (filtersChanged)
    {
      _activeDeblock = s.Deblock;
      _activeDespeckle = s.Despeckle;
      _filters = BuildFilters(s.Deblock, s.Despeckle);
    }

    _logger.LogDebug("Motion grid processor reconfigured: filters=[{Filters}] algorithm={Algorithm}",
      string.Join(", ", _filters.Select(f => f.GetType().Name)), _algorithm.GetType().Name);
  }

  private void FlushChain()
  {
    for (var i = 0; i < _filters.Length; i++)
    {
      _filters[i].Flush();
      DrainFrom(i);
    }
  }

  private void DrainFilters()
  {
    for (var i = 0; i < _filters.Length - 1; i++)
      DrainFrom(i);

    var last = _filters[^1];
    while (last.TryReceive(out var intermediate))
      _algorithm.Feed(intermediate);
  }

  private void DrainFrom(int index)
  {
    var next = index + 1 < _filters.Length ? _filters[index + 1] : null;
    while (_filters[index].TryReceive(out var intermediate))
    {
      if (next != null)
        next.Feed(intermediate);
      else
        _algorithm.Feed(intermediate);
    }
  }

  private static IFilter[] BuildFilters(bool deblock, bool despeckle)
  {
    var filters = new List<IFilter>();
    if (deblock) filters.Add(new TemporalDeblock());
    if (despeckle) filters.Add(new Despeckle());
    return filters.ToArray();
  }

  private static IMotionGridAlgorithm BuildAlgorithm(string name, int windowFrames) =>
    name switch
    {
      DetectionAlgorithm.Raw => new RawPassthrough(),
      DetectionAlgorithm.Gather => new Gather(windowFrames),
      DetectionAlgorithm.Phosphor => new Phosphor(windowFrames),
      _ => throw new InvalidOperationException($"Unknown algorithm '{name}'.")
    };
}
