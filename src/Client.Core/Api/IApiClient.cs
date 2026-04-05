using Shared.Models;
using Shared.Models.Dto;

namespace Client.Core.Api;

public interface IApiClient
{
  Task<OneOf<IReadOnlyList<CameraListItem>, Error>> GetCamerasAsync(
    string? status = null, CancellationToken ct = default);
  Task<OneOf<CameraListItem, Error>> GetCameraAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<CameraListItem, Error>> CreateCameraAsync(
    CreateCameraRequest request, CancellationToken ct = default);
  Task<OneOf<CameraListItem, Error>> UpdateCameraAsync(
    Guid id, UpdateCameraRequest request, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteCameraAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<ProbeResponse, Error>> ProbeCameraAsync(
    ProbeRequest request, CancellationToken ct = default);
  Task<OneOf<CameraListItem, Error>> RefreshCameraAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<Success, Error>> RestartCameraAsync(Guid id, CancellationToken ct = default);

  Task<OneOf<IReadOnlyList<ClientListItem>, Error>> GetClientsAsync(CancellationToken ct = default);
  Task<OneOf<ClientListItem, Error>> GetClientAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<ClientListItem, Error>> UpdateClientAsync(
    Guid id, UpdateClientRequest request, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteClientAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<StartEnrollmentResponse, Error>> StartEnrollmentAsync(CancellationToken ct = default);

  Task<OneOf<IReadOnlyList<DiscoveredCameraDto>, Error>> DiscoverAsync(
    DiscoveryRequest request, CancellationToken ct = default);

  Task<OneOf<IReadOnlyList<RecordingSegmentDto>, Error>> GetRecordingsAsync(
    Guid cameraId, ulong from, ulong to, string? profile = null,
    CancellationToken ct = default);
  Task<OneOf<TimelineResponse, Error>> GetTimelineAsync(
    Guid cameraId, ulong from, ulong to, string? profile = null,
    CancellationToken ct = default);

  Task<OneOf<IReadOnlyList<EventDto>, Error>> GetEventsAsync(
    Guid? cameraId = null, string? type = null, ulong? from = null, ulong? to = null,
    int limit = 100, int offset = 0, CancellationToken ct = default);
  Task<OneOf<EventDto, Error>> GetEventAsync(Guid id, CancellationToken ct = default);

  Task<OneOf<RetentionPolicy, Error>> GetRetentionAsync(CancellationToken ct = default);
  Task<OneOf<Success, Error>> UpdateRetentionAsync(
    RetentionPolicy policy, CancellationToken ct = default);

  Task<OneOf<HealthResponse, Error>> GetHealthAsync(CancellationToken ct = default);
  Task<OneOf<StorageResponse, Error>> GetStorageAsync(CancellationToken ct = default);
  Task<OneOf<ServerSettings, Error>> GetSettingsAsync(CancellationToken ct = default);
  Task<OneOf<Success, Error>> UpdateSettingsAsync(
    ServerSettings settings, CancellationToken ct = default);
  Task<OneOf<Success, Error>> GenerateCertsAsync(CancellationToken ct = default);

  Task<OneOf<IReadOnlyList<PluginListItem>, Error>> GetPluginsAsync(
    string? type = null, CancellationToken ct = default);
  Task<OneOf<PluginListItem, Error>> GetPluginAsync(string id, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<SettingGroup>, Error>> GetPluginConfigSchemaAsync(
    string id, CancellationToken ct = default);
  Task<OneOf<IReadOnlyDictionary<string, System.Text.Json.JsonElement>, Error>> GetPluginConfigAsync(
    string id, CancellationToken ct = default);
  Task<OneOf<Success, Error>> UpdatePluginConfigAsync(
    string id, Dictionary<string, System.Text.Json.JsonElement> values, CancellationToken ct = default);
  Task<OneOf<Success, Error>> ValidatePluginFieldAsync(
    string id, ValidateFieldRequest request, CancellationToken ct = default);
  Task<OneOf<Success, Error>> StartPluginAsync(string id, CancellationToken ct = default);
  Task<OneOf<Success, Error>> StopPluginAsync(string id, CancellationToken ct = default);
}
