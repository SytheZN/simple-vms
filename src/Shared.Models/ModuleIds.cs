namespace Shared.Models;

public static class ModuleIds
{
  public const ushort PluginRangeStart = 0x1000;
  public const ushort PluginRangeEnd = 0xFFFF;

  public const ushort PluginSqliteCamera = 0x1001;
  public const ushort PluginSqliteStream = 0x1002;
  public const ushort PluginSqliteSegment = 0x1003;
  public const ushort PluginSqliteKeyframe = 0x1004;
  public const ushort PluginSqliteEvent = 0x1005;
  public const ushort PluginSqliteClient = 0x1006;
  public const ushort PluginSqliteSettings = 0x1007;
  public const ushort PluginSqlitePluginData = 0x1008;
  public const ushort PluginSqliteMigration = 0x1009;
}
