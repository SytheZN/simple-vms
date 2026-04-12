using System.Collections.ObjectModel;
using Client.Core.Api;
using Client.Core.Events;
using Microsoft.Extensions.Logging;
using Shared.Models.Dto;
using Shared.Protocol;

namespace Client.Core.ViewModels;

public sealed class EventsViewModel : ViewModelBase, IDisposable
{
  private readonly IApiClient _api;
  private readonly IEventService _events;
  private readonly ILogger<EventsViewModel> _logger;

  private Guid? _filterCameraId;
  private string? _filterType;
  private ulong _filterFrom = DefaultFrom();
  private ulong _filterTo = DefaultTo();
  private int _limit = 100;
  private int _offset;
  private bool _hasMore;

  private static ulong DefaultFrom() =>
    (ulong)(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds() * 1000);

  private static ulong DefaultTo() =>
    (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);

  public ObservableCollection<EventDto> Events { get; } = [];

  public Guid? FilterCameraId
  {
    get => _filterCameraId;
    set => SetProperty(ref _filterCameraId, value);
  }

  public string? FilterType
  {
    get => _filterType;
    set => SetProperty(ref _filterType, value);
  }

  public ulong FilterFrom
  {
    get => _filterFrom;
    set => SetProperty(ref _filterFrom, value);
  }

  public ulong FilterTo
  {
    get => _filterTo;
    set => SetProperty(ref _filterTo, value);
  }

  public bool HasMore
  {
    get => _hasMore;
    private set => SetProperty(ref _hasMore, value);
  }

  public int Offset => _offset;

  public EventsViewModel(IApiClient api, IEventService events, ILogger<EventsViewModel> logger)
  {
    _api = api;
    _events = events;
    _logger = logger;
    _events.OnEvent += OnRealtimeEvent;
  }

  public async Task LoadAsync(CancellationToken ct)
  {
    _filterFrom = DefaultFrom();
    _filterTo = DefaultTo();
    _offset = 0;
    Events.Clear();
    await FetchPageAsync(ct);
  }

  public async Task PrevPageAsync(CancellationToken ct)
  {
    _offset = Math.Max(0, _offset - _limit);
    Events.Clear();
    await FetchPageAsync(ct);
  }

  public async Task NextPageAsync(CancellationToken ct)
  {
    _offset += _limit;
    Events.Clear();
    await FetchPageAsync(ct);
  }

  private async Task FetchPageAsync(CancellationToken ct)
  {
    _logger.LogDebug("Fetching events offset={Offset} limit={Limit}", _offset, _limit);
    var result = await _api.GetEventsAsync(
      _filterCameraId, _filterType, _filterFrom, _filterTo,
      _limit, _offset, ct);
    result.Switch(
      events => RunOnUiThread(() =>
      {
        ClearError();
        foreach (var evt in events)
          Events.Add(evt);
        HasMore = events.Count >= _limit;
        _logger.LogDebug("Fetched {Count} events", events.Count);
      }),
      error =>
      {
        _logger.LogWarning("Failed to fetch events: {Message}", error.Message);
        RunOnUiThread(() => SetError(error));
      });
  }

  private void OnRealtimeEvent(EventChannelMessage msg, EventChannelFlags flags)
  {
    if ((flags & EventChannelFlags.Start) == 0) return;

    if (_filterCameraId != null && _filterCameraId != msg.CameraId) return;
    if (_filterType != null && _filterType != msg.Type) return;

    var dto = new EventDto
    {
      Id = msg.Id,
      CameraId = msg.CameraId,
      Type = msg.Type,
      StartTime = msg.StartTime,
      EndTime = msg.EndTime,
      Metadata = msg.Metadata
    };
    RunOnUiThread(() => Events.Insert(0, dto));
  }

  public void Dispose() => _events.OnEvent -= OnRealtimeEvent;
}
