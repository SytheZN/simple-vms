using System.Reflection;

namespace Client.Core.Platform;

public sealed record DiagnosticsInfo(string? LogFilePath)
{
  public string Version { get; } = typeof(DiagnosticsInfo).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion?.Split('+')[0] ?? "0.0.0-unknown";
}
