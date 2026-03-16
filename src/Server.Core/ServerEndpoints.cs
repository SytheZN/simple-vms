namespace Server.Core;

public sealed class ServerEndpoints
{
  public string[] HttpAddresses { get; set; } = [];
  public int QuicPort { get; set; }
}
