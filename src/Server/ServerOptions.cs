namespace Server;

public sealed class ServerOptions
{
  public string DataPath { get; set; } = "./data";
  public int QuicPort { get; set; } = 443;
  public int HttpPort { get; set; } = 8080;
  public string Bind { get; set; } = "0.0.0.0";

  public string CertsPath => Path.Combine(DataPath, "certs");
  public string PluginsPath => Path.Combine(DataPath, "plugins");
}
