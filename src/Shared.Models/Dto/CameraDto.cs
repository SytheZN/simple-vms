namespace Shared.Models.Dto;

public sealed class CameraListItem
{
  public required Guid Id { get; init; }
  public required string Name { get; init; }
  public required string Address { get; init; }
  public required string Status { get; init; }
  public required string ProviderId { get; init; }
  public required IReadOnlyList<StreamProfileDto> Streams { get; init; }
  public required string[] Capabilities { get; init; }
  public Dictionary<string, string>? Config { get; init; }
  public int? SegmentDuration { get; init; }
  public string? RetentionMode { get; init; }
  public long? RetentionValue { get; init; }
}

public sealed class StreamProfileDto
{
  public required string Profile { get; init; }
  public required StreamKind Kind { get; init; }
  public required string Codec { get; init; }
  public required string Resolution { get; init; }
  public required decimal Fps { get; init; }
}

public sealed class CreateCameraRequest
{
  public required string Address { get; init; }
  public string? ProviderId { get; init; }
  public CredentialsDto? Credentials { get; init; }
  public string? Name { get; init; }
  public int? RtspPortOverride { get; init; }
}

public sealed class ProbeRequest
{
  public required string Address { get; init; }
  public string? ProviderId { get; init; }
  public CredentialsDto? Credentials { get; init; }
}

public sealed class ProbeResponse
{
  public required string Name { get; init; }
  public required IReadOnlyList<StreamProfileDto> Streams { get; init; }
  public required string[] Capabilities { get; init; }
  public required Dictionary<string, string> Config { get; init; }
}

public sealed class UpdateCameraRequest
{
  public string? Name { get; init; }
  public string? Address { get; init; }
  public string? ProviderId { get; init; }
  public CredentialsDto? Credentials { get; init; }
  public int? RtspPortOverride { get; init; }
}

public sealed class CredentialsDto
{
  public required string Username { get; init; }
  public required string Password { get; init; }
}

public sealed class CameraConfigSchema
{
  public Dictionary<string, IReadOnlyList<SettingGroup>> Camera { get; init; } = new();
  public Dictionary<string, Dictionary<string, IReadOnlyList<SettingGroup>>> Streams { get; init; } = new();
}

public sealed class CameraConfigValues
{
  public Dictionary<string, Dictionary<string, string>> Camera { get; init; } = new();
  public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Streams { get; init; } = new();
}
