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
}

public sealed class StreamProfileDto
{
  public required string Profile { get; init; }
  public required string Codec { get; init; }
  public required string Resolution { get; init; }
  public required int Fps { get; init; }
  public required bool RecordingEnabled { get; init; }
}

public sealed class CreateCameraRequest
{
  public required string Address { get; init; }
  public string? ProviderId { get; init; }
  public CredentialsDto? Credentials { get; init; }
  public string? Name { get; init; }
}

public sealed class UpdateCameraRequest
{
  public string? Name { get; init; }
  public CredentialsDto? Credentials { get; init; }
  public IReadOnlyList<UpdateStreamConfig>? Streams { get; init; }
  public int? SegmentDuration { get; init; }
  public RetentionOverride? Retention { get; init; }
}

public sealed class UpdateStreamConfig
{
  public required string Profile { get; init; }
  public required bool RecordingEnabled { get; init; }
}

public sealed class CredentialsDto
{
  public required string Username { get; init; }
  public required string Password { get; init; }
}

public sealed class RetentionOverride
{
  public required string Mode { get; init; }
  public required long Value { get; init; }
}
