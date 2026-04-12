using System.Collections.ObjectModel;
using Client.Core.Api;
using Microsoft.Extensions.Logging;
using Shared.Models.Dto;

namespace Client.Core.ViewModels;

public sealed class TimelineViewModel : ViewModelBase, IDisposable
{
  private readonly IApiClient _api;
  private readonly ILogger<TimelineViewModel> _logger;

  private Guid _cameraId;
  private string _profile = "main";
  private ulong _currentPosition;
  private double _zoomLevel = 1.0;
  private ulong _visibleFrom;
  private ulong _visibleTo;
  private CancellationTokenSource? _loadCts;

  public ObservableCollection<TimelineSpan> Spans { get; } = [];
  public ObservableCollection<TimelineEvent> Events { get; } = [];

  public ulong CurrentPosition
  {
    get => _currentPosition;
    set => SetProperty(ref _currentPosition, value);
  }

  public double ZoomLevel
  {
    get => _zoomLevel;
    set
    {
      if (SetProperty(ref _zoomLevel, Math.Max(0.1, value)))
        _ = LoadDebouncedAsync();
    }
  }

  public ulong VisibleFrom
  {
    get => _visibleFrom;
    set => SetProperty(ref _visibleFrom, value);
  }

  public ulong VisibleTo
  {
    get => _visibleTo;
    set => SetProperty(ref _visibleTo, value);
  }

  public TimelineViewModel(IApiClient api, ILogger<TimelineViewModel> logger)
  {
    _api = api;
    _logger = logger;
  }

  public void Configure(Guid cameraId, string profile)
  {
    _cameraId = cameraId;
    _profile = profile;
  }

  public async Task LoadAsync(CancellationToken ct)
  {
    if (_cameraId == Guid.Empty) return;
    if (_visibleFrom == 0 && _visibleTo == 0) return;

    var result = await _api.GetTimelineAsync(_cameraId, _visibleFrom, _visibleTo, _profile, ct);
    result.Switch(
      timeline => RunOnUiThread(() =>
      {
        ClearError();
        Spans.Clear();
        foreach (var span in timeline.Spans)
          Spans.Add(span);

        Events.Clear();
        foreach (var evt in timeline.Events)
          Events.Add(evt);

        OnPropertyChanged(nameof(Spans));
      }),
      error =>
      {
        _logger.LogWarning("Failed to load timeline: {Message}", error.Message);
        RunOnUiThread(() => SetError(error));
      });
  }

  public void SetVisibleRange(ulong from, ulong to)
  {
    VisibleFrom = from;
    VisibleTo = to;
    _ = LoadDebouncedAsync();
  }

  private async Task LoadDebouncedAsync()
  {
    var old = _loadCts;
    _loadCts = new CancellationTokenSource();
    old?.Cancel();
    old?.Dispose();
    try
    {
      await Task.Delay(150, _loadCts.Token);
      await LoadAsync(_loadCts.Token);
    }
    catch (OperationCanceledException) { }
  }

  public void Dispose()
  {
    _loadCts?.Cancel();
    _loadCts?.Dispose();
  }
}
