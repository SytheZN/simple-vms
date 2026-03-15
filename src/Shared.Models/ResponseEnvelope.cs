namespace Shared.Models;

public sealed class ResponseEnvelope
{
  public required Result Result { get; init; }
  public required DebugTag DebugTag { get; init; }
  public string? Message { get; init; }
  public object? Body { get; init; }
}

public sealed class ResponseEnvelope<T>
{
  public required Result Result { get; init; }
  public required DebugTag DebugTag { get; init; }
  public string? Message { get; init; }
  public T? Body { get; init; }
}
