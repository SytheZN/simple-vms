namespace Shared.Models;

public static class ModuleIds
{
  public const ushort Api = 0x0001;
  public const ushort Enrollment = 0x0002;
  public const ushort CameraManagement = 0x0003;
  public const ushort ClientManagement = 0x0004;
  public const ushort Discovery = 0x0005;
  public const ushort Recording = 0x0006;
  public const ushort Events = 0x0007;
  public const ushort Retention = 0x0008;
  public const ushort SystemManagement = 0x0009;
  public const ushort PluginManagement = 0x000A;
  public const ushort Setup = 0x000B;

  public const ushort PluginRangeStart = 0x1000;
  public const ushort PluginRangeEnd = 0xFFFF;

  public const ushort PluginSqliteCamera = 0x1001;
  public const ushort PluginSqliteStream = 0x1002;
  public const ushort PluginSqliteSegment = 0x1003;
  public const ushort PluginSqliteKeyframe = 0x1004;
  public const ushort PluginSqliteEvent = 0x1005;
  public const ushort PluginSqliteClient = 0x1006;
  public const ushort PluginSqliteConfig = 0x1007;
  public const ushort PluginSqliteDataStore = 0x1008;
  public const ushort PluginSqliteMigration = 0x1009;

  public const ushort PluginOnvifDiscovery = 0x1010;
  public const ushort PluginOnvifDevice = 0x1011;
  public const ushort PluginOnvifMedia = 0x1012;
  public const ushort PluginOnvifEvents = 0x1013;
  public const ushort PluginOnvifSoap = 0x1014;

  public const ushort Streaming = 0x000C;
  public const ushort LiveStreaming = 0x000D;
  public const ushort ApiWebSocketStream = 0x000E;

  public const ushort PluginRtspCapture = 0x1020;
  public const ushort PluginFmp4 = 0x1030;
  public const ushort PluginFilesystemStorage = 0x1040;
}
