using System.Reflection;

namespace Server.Core;

public sealed class SystemHealth
{
  private readonly long _startTicks = Environment.TickCount64;
  private volatile string[]? _missingSettings;
  private volatile string _status = "missing-certs";

  public string Status => _status;
  public int Uptime => (int)((Environment.TickCount64 - _startTicks) / 1000);
  public string Version { get; } = Assembly.GetEntryAssembly()
    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion?.Split('+')[0] ?? "0.0.0-unknown";

  public string[]? MissingSettings => _missingSettings;

  public void SetMissingSettings(string[]? value) => _missingSettings = value;

  public void TransitionToStarting() => _status = "starting";
  public void TransitionToHealthy() => _status = "healthy";
  public void TransitionToDegraded() => _status = "degraded";
  public void TransitionToUnhealthy() => _status = "unhealthy";
}
