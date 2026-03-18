using System.Reflection;

namespace Server.Core;

public sealed class SystemHealth
{
  private readonly long _startTicks = Environment.TickCount64;

  public string Status { get; private set; } = "missing-certs";
  public int Uptime => (int)((Environment.TickCount64 - _startTicks) / 1000);
  public string Version { get; } = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

  public void TransitionToStarting()
  {
    Status = "starting";
  }

  public void TransitionToHealthy()
  {
    Status = "healthy";
  }

  public void TransitionToDegraded()
  {
    Status = "degraded";
  }

  public void TransitionToUnhealthy()
  {
    Status = "unhealthy";
  }
}
