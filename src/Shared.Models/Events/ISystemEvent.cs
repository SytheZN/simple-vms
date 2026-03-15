namespace Shared.Models.Events;

public interface ISystemEvent
{
  ulong Timestamp { get; }
}
