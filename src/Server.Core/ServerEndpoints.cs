namespace Server.Core;

public sealed class ServerEndpoints
{
  public string[] HttpAddresses { get; set; } = [];
  public int TunnelPort { get; set; }
}
