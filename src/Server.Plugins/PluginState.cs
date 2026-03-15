namespace Server.Plugins;

public enum PluginState
{
  Discovered,
  Loaded,
  Configured,
  Starting,
  Running,
  Stopping,
  Stopped,
  Error
}
