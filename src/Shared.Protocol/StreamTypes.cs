namespace Shared.Protocol;

public static class StreamTypes
{
  public const ushort Keepalive = 0x0100;
  public const ushort ApiRequest = 0x0200;
  public const ushort LiveSubscribe = 0x0300;
  public const ushort Playback = 0x0301;
  public const ushort EventChannel = 0x0400;

  public const ushort ControlRangeStart = 0x0100;
  public const ushort ControlRangeEnd = 0x01FF;
  public const ushort ApiRangeStart = 0x0200;
  public const ushort ApiRangeEnd = 0x02FF;
  public const ushort VideoRangeStart = 0x0300;
  public const ushort VideoRangeEnd = 0x03FF;
  public const ushort EventRangeStart = 0x0400;
  public const ushort EventRangeEnd = 0x04FF;
  public const ushort PluginRangeStart = 0x1000;
  public const ushort PluginRangeEnd = 0x1FFF;

  public static bool IsValid(ushort type) =>
    type is (>= ControlRangeStart and <= EventRangeEnd)
         or (>= PluginRangeStart and <= PluginRangeEnd);

  public static bool IsPlugin(ushort type) =>
    type is >= PluginRangeStart and <= PluginRangeEnd;
}
