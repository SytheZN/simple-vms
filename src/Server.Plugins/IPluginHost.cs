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
  IReadOnlyList<IVideoAnalyzer> VideoAnalyzers { get; }
  IReadOnlyList<IStorageProvider> StorageProviders { get; }
  IReadOnlyList<IAuthProvider> AuthProviders { get; }
  IReadOnlyList<IAuthzProvider> AuthzProviders { get; }
  IStreamFormat? FindFormat(Type inputType);
  void SetStreamTap(IStreamTap streamTap);
  void SetCameraRegistry(ICameraRegistry cameraRegistry);
  void SetRecordingAccess(IRecordingAccess recordingAccess);
  [RequiresUnreferencedCode("Plugin discovery loads assemblies dynamically")]
  void Discover(string pluginsPath);
  [RequiresUnreferencedCode("Plugin initialization uses dynamic type instantiation")]
  void Initialize(bool dataOnly = false);
  [RequiresUnreferencedCode("Plugin types are instantiated dynamically")]
  void ResetErrored();
  Task StartAsync(CancellationToken ct);
  Task StopAsync();
}
