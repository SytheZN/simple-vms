using Shared.Models.Entities;

namespace Shared.Models;

public interface IEventFilter
{
  string FilterId { get; }
  Task<EventFilterResult> ProcessAsync(CameraEvent rawEvent, CancellationToken ct);
}