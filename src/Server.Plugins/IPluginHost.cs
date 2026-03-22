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
  void Discover(string pluginsPath);
  void Initialize(bool dataOnly = false);
  Task StartAsync(CancellationToken ct);
  Task StopAsync();
}
