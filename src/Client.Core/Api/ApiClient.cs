using System.Text.Json;
using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;

namespace Client.Core.Api;

public sealed class ApiClient : IApiClient
{
  private static readonly JsonSerializerOptions JsonOptions = ClientJsonContext.Default.Options;

  private readonly ITunnelService _tunnel;
  private readonly ILogger<ApiClient> _logger;

  public ApiClient(ITunnelService tunnel, ILogger<ApiClient> logger)
  {
    _tunnel = tunnel;
    _logger = logger;
  }

  public Task<OneOf<IReadOnlyList<CameraListItem>, Error>> GetCamerasAsync(
    string? status, CancellationToken ct)
  {
    var path = status != null ? $"/api/v1/cameras?status={Uri.EscapeDataString(status)}" : "/api/v1/cameras";
    return ExecuteAsync<IReadOnlyList<CameraListItem>>("GET", path, null, ct);
  }

  public Task<OneOf<CameraListItem, Error>> GetCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync<CameraListItem>("GET", $"/api/v1/cameras/{id}", null, ct);

  public Task<OneOf<CameraListItem, Error>> CreateCameraAsync(
    CreateCameraRequest request, CancellationToken ct) =>
    ExecuteAsync<CameraListItem>("POST", "/api/v1/cameras", request, ct);

  public Task<OneOf<CameraListItem, Error>> UpdateCameraAsync(
    Guid id, UpdateCameraRequest request, CancellationToken ct) =>
    ExecuteAsync<CameraListItem>("PUT", $"/api/v1/cameras/{id}", request, ct);

  public Task<OneOf<Success, Error>> DeleteCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteVoidAsync("DELETE", $"/api/v1/cameras/{id}", null, ct);

  public Task<OneOf<ProbeResponse, Error>> ProbeCameraAsync(
    ProbeRequest request, CancellationToken ct) =>
    ExecuteAsync<ProbeResponse>("POST", "/api/v1/cameras/probe", request, ct);

  public Task<OneOf<CameraListItem, Error>> RefreshCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync<CameraListItem>("POST", $"/api/v1/cameras/{id}/refresh", null, ct);

  public Task<OneOf<Success, Error>> RestartCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/cameras/{id}/restart", null, ct);

  public Task<OneOf<IReadOnlyList<ClientListItem>, Error>> GetClientsAsync(CancellationToken ct) =>
    ExecuteAsync<IReadOnlyList<ClientListItem>>("GET", "/api/v1/clients", null, ct);

  public Task<OneOf<ClientListItem, Error>> GetClientAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync<ClientListItem>("GET", $"/api/v1/clients/{id}", null, ct);

  public Task<OneOf<ClientListItem, Error>> UpdateClientAsync(
    Guid id, UpdateClientRequest request, CancellationToken ct) =>
    ExecuteAsync<ClientListItem>("PUT", $"/api/v1/clients/{id}", request, ct);

  public Task<OneOf<Success, Error>> DeleteClientAsync(Guid id, CancellationToken ct) =>
    ExecuteVoidAsync("DELETE", $"/api/v1/clients/{id}", null, ct);

  public Task<OneOf<StartEnrollmentResponse, Error>> StartEnrollmentAsync(CancellationToken ct) =>
    ExecuteAsync<StartEnrollmentResponse>("POST", "/api/v1/clients/enroll", null, ct);

  public Task<OneOf<IReadOnlyList<DiscoveredCameraDto>, Error>> DiscoverAsync(
    DiscoveryRequest request, CancellationToken ct) =>
    ExecuteAsync<IReadOnlyList<DiscoveredCameraDto>>("POST", "/api/v1/discovery", request, ct);

  public Task<OneOf<IReadOnlyList<RecordingSegmentDto>, Error>> GetRecordingsAsync(
    Guid cameraId, ulong from, ulong to, string? profile, CancellationToken ct)
  {
    var path = BuildPath($"/api/v1/recordings/{cameraId}",
      ("profile", profile), ("from", from.ToString()), ("to", to.ToString()));
    return ExecuteAsync<IReadOnlyList<RecordingSegmentDto>>("GET", path, null, ct);
  }

  public Task<OneOf<TimelineResponse, Error>> GetTimelineAsync(
    Guid cameraId, ulong from, ulong to, string? profile, CancellationToken ct)
  {
    var path = BuildPath($"/api/v1/recordings/{cameraId}/timeline",
      ("profile", profile), ("from", from.ToString()), ("to", to.ToString()));
    return ExecuteAsync<TimelineResponse>("GET", path, null, ct);
  }

  public Task<OneOf<IReadOnlyList<EventDto>, Error>> GetEventsAsync(
    Guid? cameraId, string? type, ulong? from, ulong? to,
    int limit, int offset, CancellationToken ct)
  {
    var path = BuildPath("/api/v1/events",
      ("cameraId", cameraId?.ToString()), ("type", type),
      ("from", from?.ToString()), ("to", to?.ToString()),
      ("limit", limit != 100 ? limit.ToString() : null),
      ("offset", offset != 0 ? offset.ToString() : null));
    return ExecuteAsync<IReadOnlyList<EventDto>>("GET", path, null, ct);
  }

  public Task<OneOf<EventDto, Error>> GetEventAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync<EventDto>("GET", $"/api/v1/events/{id}", null, ct);

  public Task<OneOf<RetentionPolicy, Error>> GetRetentionAsync(CancellationToken ct) =>
    ExecuteAsync<RetentionPolicy>("GET", "/api/v1/retention", null, ct);

  public Task<OneOf<Success, Error>> UpdateRetentionAsync(
    RetentionPolicy policy, CancellationToken ct) =>
    ExecuteVoidAsync("PUT", "/api/v1/retention", policy, ct);

  public Task<OneOf<HealthResponse, Error>> GetHealthAsync(CancellationToken ct) =>
    ExecuteAsync<HealthResponse>("GET", "/api/v1/system/health", null, ct);

  public Task<OneOf<StorageResponse, Error>> GetStorageAsync(CancellationToken ct) =>
    ExecuteAsync<StorageResponse>("GET", "/api/v1/system/storage", null, ct);

  public Task<OneOf<ServerSettings, Error>> GetSettingsAsync(CancellationToken ct) =>
    ExecuteAsync<ServerSettings>("GET", "/api/v1/system/settings", null, ct);

  public Task<OneOf<Success, Error>> UpdateSettingsAsync(
    ServerSettings settings, CancellationToken ct) =>
    ExecuteVoidAsync("PUT", "/api/v1/system/settings", settings, ct);

  public Task<OneOf<Success, Error>> GenerateCertsAsync(CancellationToken ct) =>
    ExecuteVoidAsync("POST", "/api/v1/system/certs", null, ct);

  public Task<OneOf<IReadOnlyList<PluginListItem>, Error>> GetPluginsAsync(
    string? type, CancellationToken ct)
  {
    var path = type != null ? $"/api/v1/plugins?type={Uri.EscapeDataString(type)}" : "/api/v1/plugins";
    return ExecuteAsync<IReadOnlyList<PluginListItem>>("GET", path, null, ct);
  }

  public Task<OneOf<PluginListItem, Error>> GetPluginAsync(string id, CancellationToken ct) =>
    ExecuteAsync<PluginListItem>("GET", $"/api/v1/plugins/{Uri.EscapeDataString(id)}", null, ct);

  public Task<OneOf<IReadOnlyList<SettingGroup>, Error>> GetPluginConfigSchemaAsync(
    string id, CancellationToken ct) =>
    ExecuteAsync<IReadOnlyList<SettingGroup>>("OPTIONS", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config", null, ct);

  public Task<OneOf<IReadOnlyDictionary<string, JsonElement>, Error>> GetPluginConfigAsync(
    string id, CancellationToken ct) =>
    ExecuteAsync<IReadOnlyDictionary<string, JsonElement>>(
      "GET", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config", null, ct);

  public Task<OneOf<Success, Error>> UpdatePluginConfigAsync(
    string id, Dictionary<string, JsonElement> values, CancellationToken ct) =>
    ExecuteVoidAsync("PUT", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config", values, ct);

  public Task<OneOf<Success, Error>> ValidatePluginFieldAsync(
    string id, ValidateFieldRequest request, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config/validate", request, ct);

  public Task<OneOf<Success, Error>> StartPluginAsync(string id, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/start", null, ct);

  public Task<OneOf<Success, Error>> StopPluginAsync(string id, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/stop", null, ct);

  private async Task<OneOf<T, Error>> ExecuteAsync<T>(
    string method, string path, object? body, CancellationToken ct)
  {
    var sendResult = await SendRequestAsync(method, path, body, ct);
    if (sendResult.IsT1)
      return sendResult.AsT1;

    var response = sendResult.AsT0;
    var result = (Result)response.Result;

    if (result != Result.Success && result != Result.Created)
    {
      _logger.LogError("{Method} {Path} returned {Result}: {Message}",
        method, path, result, response.Message);
      return new Error(result, new DebugTag(response.DebugTag), response.Message ?? "");
    }

    if (response.Body == null)
    {
      _logger.LogError("{Method} {Path} returned success but no body", method, path);
      return Error.Create(ClientModuleIds.Api, 0x0001, Result.InternalError, "Expected response body");
    }

    var value = (T?)JsonSerializer.Deserialize(response.Body, JsonOptions.GetTypeInfo(typeof(T)));
    if (value == null)
    {
      _logger.LogError("{Method} {Path} response body failed to deserialize as {Type}",
        method, path, typeof(T).Name);
      return Error.Create(ClientModuleIds.Api, 0x0002, Result.InternalError, "Failed to deserialize response");
    }

    _logger.LogDebug("{Method} {Path} succeeded", method, path);
    return value;
  }

  private async Task<OneOf<Success, Error>> ExecuteVoidAsync(
    string method, string path, object? body, CancellationToken ct)
  {
    var sendResult = await SendRequestAsync(method, path, body, ct);
    if (sendResult.IsT1)
      return sendResult.AsT1;

    var response = sendResult.AsT0;
    var result = (Result)response.Result;

    if (result != Result.Success && result != Result.Created)
    {
      _logger.LogError("{Method} {Path} returned {Result}: {Message}",
        method, path, result, response.Message);
      return new Error(result, new DebugTag(response.DebugTag), response.Message ?? "");
    }

    _logger.LogDebug("{Method} {Path} succeeded", method, path);
    return new Success();
  }

  private async Task<OneOf<ApiResponseMessage, Error>> SendRequestAsync(
    string method, string path, object? body, CancellationToken ct)
  {
    var generationBefore = _tunnel.Generation;

    var request = new ApiRequestMessage
    {
      Method = method,
      Path = path,
      Body = body != null ? JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions.GetTypeInfo(body.GetType())) : null
    };

    var requestPayload = MessagePackSerializer.Serialize(request, ProtocolSerializer.Options);
    await using var stream = await _tunnel.OpenStreamAsync(StreamTypes.ApiRequest, requestPayload, ct);

    MuxMessage msg;
    try
    {
      msg = await stream.Reader.ReadAsync(ct);
    }
    catch (OperationCanceledException) when (_tunnel.Generation != generationBefore)
    {
      _logger.LogWarning("{Method} {Path} discarded - connection lost during request", method, path);
      return Error.Create(ClientModuleIds.Api, 0x0003, Result.Unavailable, "Connection was lost during request");
    }
    catch (System.Threading.Channels.ChannelClosedException)
    {
      _logger.LogWarning("{Method} {Path} discarded - stream closed during request", method, path);
      return Error.Create(ClientModuleIds.Api, 0x0004, Result.Unavailable, "Stream closed during request");
    }

    if (_tunnel.Generation != generationBefore)
    {
      _logger.LogWarning("{Method} {Path} discarded - connection generation changed", method, path);
      return Error.Create(ClientModuleIds.Api, 0x0005, Result.Unavailable, "Connection generation changed during request");
    }

    return MessagePackSerializer.Deserialize<ApiResponseMessage>(msg.Payload, ProtocolSerializer.Options);
  }

  private static string BuildPath(string basePath, params (string Key, string? Value)[] queryParams)
  {
    var pairs = new List<string>();
    foreach (var (key, value) in queryParams)
    {
      if (value != null)
        pairs.Add($"{key}={Uri.EscapeDataString(value)}");
    }
    return pairs.Count > 0 ? $"{basePath}?{string.Join('&', pairs)}" : basePath;
  }
}
