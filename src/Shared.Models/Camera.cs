namespace Shared.Models;

public sealed class Camera
{
  public required Guid Id { get; set; }
  public required string Name { get; set; }
  public required string Address { get; set; }
  public required string ProviderId { get; set; }
  public byte[]? Credentials { get; set; }
  public int? SegmentDuration { get; set; }
  public string[] Capabilities { get; set; } = [];
  public Dictionary<string, string> Config { get; set; } = [];
  public RetentionMode RetentionMode { get; set; } = RetentionMode.Default;
  public long RetentionValue { get; set; }
  public ulong CreatedAt { get; set; }
  public ulong UpdatedAt { get; set; }
}
