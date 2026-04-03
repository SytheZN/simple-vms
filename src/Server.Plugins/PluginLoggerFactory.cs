using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Plugins;

public sealed class PluginLoggerFactory : IPluginLoggerFactory
{
  private readonly ILoggerFactory _factory;
  private readonly string _pluginId;

  public PluginLoggerFactory(ILoggerFactory factory, string pluginId)
  {
    _factory = factory;
    _pluginId = pluginId;
  }

  public ILogger CreateLogger(string categoryName) =>
    _factory.CreateLogger($"Plugin.{_pluginId}.{categoryName}");
}
