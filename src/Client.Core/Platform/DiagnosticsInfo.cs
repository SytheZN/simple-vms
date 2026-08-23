using System.Reflection;

namespace Client.Core.Platform;

public sealed record DiagnosticsInfo(string? LogFilePath)
{
  /// <summary>
  /// Read from this assembly rather than the entry assembly, which Android does not have: its
  /// process is started by the platform rather than through a managed entry point. Every project
  /// carries the same version, so any of them answers the question.
  /// </summary>
  public string Version { get; } = typeof(DiagnosticsInfo).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion?.Split('+')[0] ?? "0.0.0-unknown";
}
