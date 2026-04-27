using Microsoft.Extensions.Logging;

namespace Shared.Models;

public interface IPluginLoggerFactory
{
  ILogger CreateLogger(string categoryName);
}
