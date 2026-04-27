using Shared.Models.Entities;

namespace Shared.Models;

public sealed class EventFilterResult
{
  public required EventDecision Decision { get; init; }
  public CameraEvent? ModifiedEvent { get; init; }
}