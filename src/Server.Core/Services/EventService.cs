using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class EventService
{
  private readonly IDataProvider _data;

  public EventService(IDataProvider data)
  {
    _data = data;
  }

  public async Task<OneOf<IReadOnlyList<EventDto>, Error>> QueryAsync(
    Guid? cameraId, string? type, ulong from, ulong to,
    int limit, int offset, CancellationToken ct)
  {
    var result = await _data.Events.QueryAsync(cameraId, type, from, to, limit, offset, ct);
    return result.Match<OneOf<IReadOnlyList<EventDto>, Error>>(
      events => events.Select(ToDto).ToList(),
      error => error);
  }

  public async Task<OneOf<EventDto, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    var result = await _data.Events.GetByIdAsync(id, ct);
    return result.Match<OneOf<EventDto, Error>>(
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
}
