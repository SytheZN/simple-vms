namespace Shared.Models;

public interface IEventFilter
{
  string FilterId { get; }
  Task<EventFilterResult> ProcessAsync(CameraEvent rawEvent, CancellationToken ct);
}

public sealed class EventFilterResult
{
  public required EventDecision Decision { get; init; }
  public CameraEvent? ModifiedEvent { get; init; }
}

public enum EventDecision
{
  Pass,
  Suppress
}
