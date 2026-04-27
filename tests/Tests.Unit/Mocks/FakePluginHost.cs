using Server.Plugins;
using Shared.Models;

namespace Tests.Unit.Mocks;

internal sealed class FakePluginHost : IPluginHost
{
  public IReadOnlyList<PluginEntry> Plugins { get; init; } = [];
  public IDataProvider DataProvider { get; init; } = null!;
  public IReadOnlyList<ICaptureSource> CaptureSources { get; init; } = [];
  public IReadOnlyList<IStreamFormat> StreamFormats { get; init; } = [];
  public IReadOnlyList<ICameraProvider> CameraProviders { get; init; } = [];
  public IReadOnlyList<IEventFilter> EventFilters { get; init; } = [];
  public IReadOnlyList<INotificationSink> NotificationSinks { get; init; } = [];
  public IReadOnlyList<IDataStreamAnalyzer> Analyzers { get; init; } = [];
  public IReadOnlyList<IStorageProvider> StorageProviders { get; init; } = [];
  public IReadOnlyList<IAuthProvider> AuthProviders { get; init; } = [];
  public IReadOnlyList<IAuthzProvider> AuthzProviders { get; init; } = [];

  public IStreamFormat? FindFormat(Type inputType) =>
    StreamFormats.FirstOrDefault(f => f.InputType == inputType);

  public void SetStreamTap(IStreamTap streamTap) { }
  public void SetCameraRegistry(ICameraRegistry cameraRegistry) { }
  public void SetRecordingAccess(IRecordingAccess recordingAccess) { }
  public void DiscoverPlugins() { }
  public Task TeardownPlugins(CancellationToken ct) => Task.CompletedTask;
  public void InitializeDataPlugins() { }
  public void InitializeOtherPlugins() { }
  public Task StartConfiguredDataPlugin(CancellationToken ct) => Task.CompletedTask;
  public Task StartOtherPlugins(CancellationToken ct) => Task.CompletedTask;
}
