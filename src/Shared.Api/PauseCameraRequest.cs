namespace Shared.Api;

public sealed class PauseCameraRequest
{
  public required uint DurationSeconds { get; init; }
}
