namespace Shared.Models;

public static class ModuleIds
{
  // Core range: 0x0001 - 0x0FFF
  public const ushort Api = 0x0001;
  public const ushort Streaming = 0x0002;
  public const ushort Recording = 0x0003;
  public const ushort Storage = 0x0004;
  public const ushort Data = 0x0005;
  public const ushort Tunnel = 0x0006;
  public const ushort Onvif = 0x0007;
  public const ushort Plugins = 0x0008;
  public const ushort Server = 0x0009;
  public const ushort Client = 0x000A;

  // Plugin range: 0x1000 - 0xFFFF
  public const ushort PluginRangeStart = 0x1000;
  public const ushort PluginRangeEnd = 0xFFFF;
}
