using System.Collections.ObjectModel;
using Client.Core.Api;
using Client.Core.Events;
using Shared.Models.Dto;
using Shared.Protocol;

namespace Client.Core.ViewModels;

public sealed class EventsViewModel : ViewModelBase, IDisposable
{
  private readonly IApiClient _api;
  private readonly IEventService _events;

  private Guid? _filterCameraId;
  private string? _filterType;
  private ulong? _filterFrom;
  private ulong? _filterTo;
  private int _limit = 100;
  private int _offset;

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

  public ulong? FilterFrom
  {
    get => _filterFrom;
    set => SetProperty(ref _filterFrom, value);
  }

  public ulong? FilterTo
  {
    get => _filterTo;
    set => SetProperty(ref _filterTo, value);
  }

  public EventsViewModel(IApiClient api, IEventService events)
  {
    _api = api;
    _events = events;
    _events.OnEvent += OnRealtimeEvent;
  }

  public async Task LoadAsync(CancellationToken ct)
  {
    _offset = 0;
    Events.Clear();
    await FetchPageAsync(ct);
  }

  public async Task LoadMoreAsync(CancellationToken ct)
  {
    _offset += _limit;
    await FetchPageAsync(ct);
  }

  private async Task FetchPageAsync(CancellationToken ct)
  {
    var result = await _api.GetEventsAsync(
      _filterCameraId, _filterType, _filterFrom, _filterTo,
      _limit, _offset, ct);
    result.Switch(
      events => RunOnUiThread(() =>
      {
        foreach (var evt in events)
          Events.Add(evt);
      }),
      _ => { });
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
