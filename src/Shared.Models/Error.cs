namespace Shared.Models;

public readonly record struct Error(Result Result, DebugTag Tag, string Message)
{
  public static Error Create(ushort module, ushort code, Result result, string message) =>
    new(result, new DebugTag(module, code), message);
}
