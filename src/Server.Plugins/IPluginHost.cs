using System.Diagnostics.CodeAnalysis;
using Shared.Models;

namespace Server.Plugins;

public interface IPluginHost
{
  IReadOnlyList<PluginEntry> Plugins { get; }
  IDataProvider DataProvider { get; }
  IReadOnlyList<ICaptureSource> CaptureSources { get; }
  IReadOnlyList<IStreamFormat> StreamFormats { get; }
  IReadOnlyList<ICameraProvider> CameraProviders { get; }
  IReadOnlyList<IEventFilter> EventFilters { get; }
  IReadOnlyList<INotificationSink> NotificationSinks { get; }
  IReadOnlyList<IDataStreamAnalyzer> Analyzers { get; }
  IReadOnlyList<IStorageProvider> StorageProviders { get; }
  IReadOnlyList<IAuthProvider> AuthProviders { get; }
  IReadOnlyList<IAuthzProvider> AuthzProviders { get; }
  IStreamFormat? FindFormat(Type inputType);
  void SetStreamTap(IStreamTap streamTap);
  void SetCameraRegistry(ICameraRegistry cameraRegistry);
  void SetRecordingAccess(IRecordingAccess recordingAccess);
  [RequiresUnreferencedCode("Plugin discovery loads assemblies dynamically")]
  void DiscoverPlugins();
  Task TeardownPlugins(CancellationToken ct);
  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  void InitializeDataPlugins();
  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  void InitializeOtherPlugins();
  Task StartConfiguredDataPlugin(CancellationToken ct);
  Task StartOtherPlugins(CancellationToken ct);
}
