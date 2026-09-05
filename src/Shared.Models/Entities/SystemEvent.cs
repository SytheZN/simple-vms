namespace Shared.Models.Entities;

public sealed class SystemEvent
{
  public required Guid Id { get; set; }
  public required string Type { get; set; }
  public required string Source { get; set; }
  public required ulong Timestamp { get; set; }
  public Dictionary<string, string>? Metadata { get; set; }
}
