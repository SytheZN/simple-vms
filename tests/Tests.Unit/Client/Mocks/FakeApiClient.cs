using Client.Core.Api;
using Shared.Models;
using Shared.Models.Dto;

namespace Tests.Unit.Client.Mocks;

public class FakeApiClient : IApiClient
{
  public virtual Task<OneOf<IReadOnlyList<CameraListItem>, Error>> GetCamerasAsync(string? status, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<CameraListItem, Error>> GetCameraAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<CameraListItem, Error>> CreateCameraAsync(CreateCameraRequest r, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<CameraListItem, Error>> UpdateCameraAsync(Guid id, UpdateCameraRequest r, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> DeleteCameraAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<ProbeResponse, Error>> ProbeCameraAsync(ProbeRequest r, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<CameraListItem, Error>> RefreshCameraAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> RestartCameraAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<IReadOnlyList<ClientListItem>, Error>> GetClientsAsync(CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<ClientListItem, Error>> GetClientAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<ClientListItem, Error>> UpdateClientAsync(Guid id, UpdateClientRequest r, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> DeleteClientAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<StartEnrollmentResponse, Error>> StartEnrollmentAsync(CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<IReadOnlyList<DiscoveredCameraDto>, Error>> DiscoverAsync(DiscoveryRequest r, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<IReadOnlyList<RecordingSegmentDto>, Error>> GetRecordingsAsync(Guid cid, ulong f, ulong t, string? p, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<TimelineResponse, Error>> GetTimelineAsync(Guid cid, ulong f, ulong t, string? p, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<IReadOnlyList<EventDto>, Error>> GetEventsAsync(Guid? cid, string? ty, ulong? f, ulong? t, int l, int o, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<EventDto, Error>> GetEventAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<RetentionPolicy, Error>> GetRetentionAsync(CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> UpdateRetentionAsync(RetentionPolicy p, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<HealthResponse, Error>> GetHealthAsync(CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<StorageResponse, Error>> GetStorageAsync(CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<ServerSettings, Error>> GetSettingsAsync(CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> UpdateSettingsAsync(ServerSettings s, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> GenerateCertsAsync(CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<IReadOnlyList<PluginListItem>, Error>> GetPluginsAsync(string? ty, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<PluginListItem, Error>> GetPluginAsync(string id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<IReadOnlyList<SettingGroup>, Error>> GetPluginConfigSchemaAsync(string id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetPluginConfigAsync(string id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> UpdatePluginConfigAsync(string id, Dictionary<string, string> v, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> ValidatePluginFieldAsync(string id, ValidateFieldRequest r, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> StartPluginAsync(string id, CancellationToken ct) => throw new NotImplementedException();
  public virtual Task<OneOf<Success, Error>> StopPluginAsync(string id, CancellationToken ct) => throw new NotImplementedException();
}
