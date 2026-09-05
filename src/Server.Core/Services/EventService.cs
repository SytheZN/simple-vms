using Server.Plugins;
using Shared.Models;
using Shared.Api;
using Shared.Models.Entities;

namespace Server.Core.Services;

public sealed class EventService
{
  private readonly IPluginHost _plugins;

  public EventService(IPluginHost plugins)
  {
    _plugins = plugins;
  }

  public async Task<OneOf<IReadOnlyList<EventDto>, Error>> QueryAsync(
    Guid? cameraId, string? type, ulong from, ulong to,
    int limit, int offset, CancellationToken ct)
  {
    if (cameraId == Guid.Empty)
    {
      var systemResult = await _plugins.DataProvider!.SystemEvents.QueryAsync(
        type, from, to, limit, offset, ct);
      return systemResult.Match<OneOf<IReadOnlyList<EventDto>, Error>>(
        events => events.Select(ToDto).ToList(),
        error => error);
    }

    if (cameraId.HasValue)
    {
      var result = await _plugins.DataProvider!.Events.QueryAsync(
        cameraId, type, from, to, limit, offset, ct);
      return result.Match<OneOf<IReadOnlyList<EventDto>, Error>>(
        events => events.Select(ToDto).ToList(),
        error => error);
    }

    var reach = limit + offset;
    var cameraEvents = await _plugins.DataProvider!.Events.QueryAsync(
      null, type, from, to, reach, 0, ct);
    if (cameraEvents.IsT1) return cameraEvents.AsT1;

    var systemEvents = await _plugins.DataProvider!.SystemEvents.QueryAsync(
      type, from, to, reach, 0, ct);
    if (systemEvents.IsT1) return systemEvents.AsT1;

    return cameraEvents.AsT0.Select(ToDto)
      .Concat(systemEvents.AsT0.Select(ToDto))
      .OrderByDescending(e => e.StartTime)
      .Skip(offset)
      .Take(limit)
      .ToList();
  }

  public async Task<OneOf<EventDto, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    var result = await _plugins.DataProvider!.Events.GetByIdAsync(id, ct);
    if (result.IsT0) return ToDto(result.AsT0);

    var systemResult = await _plugins.DataProvider!.SystemEvents.GetByIdAsync(id, ct);
    return systemResult.Match<OneOf<EventDto, Error>>(
      evt => ToDto(evt),
      error => error);
  }

  private static EventDto ToDto(CameraEvent e) =>
    new()
    {
      Id = e.Id,
      CameraId = e.CameraId,
      Type = e.Type,
      StartTime = e.StartTime,
      EndTime = e.EndTime,
      Metadata = e.Metadata
    };

  private static EventDto ToDto(SystemEvent e) =>
    new()
    {
      Id = e.Id,
      CameraId = Guid.Empty,
      Type = e.Type,
      StartTime = e.Timestamp,
      Metadata = e.Metadata,
      Source = e.Source
    };
}
