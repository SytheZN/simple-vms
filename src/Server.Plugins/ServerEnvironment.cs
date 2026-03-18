using Shared.Models;

namespace Server.Plugins;

public sealed class ServerEnvironment : IServerEnvironment
{
  public string DataPath { get; }

  public ServerEnvironment(string dataPath)
  {
    DataPath = Path.GetFullPath(dataPath);
  }
}
