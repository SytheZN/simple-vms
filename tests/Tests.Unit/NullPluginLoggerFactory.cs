namespace Tests.Unit;

internal sealed class NullPluginLoggerFactory : Shared.Models.IPluginLoggerFactory
{
  public static readonly NullPluginLoggerFactory Instance = new();
  public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
    Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
