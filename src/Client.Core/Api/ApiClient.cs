using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;

namespace Client.Core.Api;

public sealed class ApiClient : IApiClient
{
  private static ClientJsonContext Json => ClientJsonContext.Default;

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
    return ExecuteAsync("GET", path, null, Json.IReadOnlyListCameraListItem, ct);
  }

  public Task<OneOf<CameraListItem, Error>> GetCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync("GET", $"/api/v1/cameras/{id}", null, Json.CameraListItem, ct);

  public Task<OneOf<CameraListItem, Error>> CreateCameraAsync(
    CreateCameraRequest request, CancellationToken ct) =>
    ExecuteAsync("POST", "/api/v1/cameras",
      Serialize(request, Json.CreateCameraRequest), Json.CameraListItem, ct);

  public Task<OneOf<CameraListItem, Error>> UpdateCameraAsync(
    Guid id, UpdateCameraRequest request, CancellationToken ct) =>
    ExecuteAsync("PUT", $"/api/v1/cameras/{id}",
      Serialize(request, Json.UpdateCameraRequest), Json.CameraListItem, ct);

  public Task<OneOf<Success, Error>> DeleteCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteVoidAsync("DELETE", $"/api/v1/cameras/{id}", null, ct);

  public Task<OneOf<ProbeResponse, Error>> ProbeCameraAsync(
    ProbeRequest request, CancellationToken ct) =>
    ExecuteAsync("POST", "/api/v1/cameras/probe",
      Serialize(request, Json.ProbeRequest), Json.ProbeResponse, ct);

  public Task<OneOf<CameraListItem, Error>> RefreshCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync("POST", $"/api/v1/cameras/{id}/refresh", null, Json.CameraListItem, ct);

  public Task<OneOf<Success, Error>> RestartCameraAsync(Guid id, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/cameras/{id}/restart", null, ct);

  public Task<OneOf<IReadOnlyList<ClientListItem>, Error>> GetClientsAsync(CancellationToken ct) =>
    ExecuteAsync("GET", "/api/v1/clients", null, Json.IReadOnlyListClientListItem, ct);

  public Task<OneOf<ClientListItem, Error>> GetClientAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync("GET", $"/api/v1/clients/{id}", null, Json.ClientListItem, ct);

  public Task<OneOf<ClientListItem, Error>> UpdateClientAsync(
    Guid id, UpdateClientRequest request, CancellationToken ct) =>
    ExecuteAsync("PUT", $"/api/v1/clients/{id}",
      Serialize(request, Json.UpdateClientRequest), Json.ClientListItem, ct);

  public Task<OneOf<Success, Error>> DeleteClientAsync(Guid id, CancellationToken ct) =>
    ExecuteVoidAsync("DELETE", $"/api/v1/clients/{id}", null, ct);

  public Task<OneOf<StartEnrollmentResponse, Error>> StartEnrollmentAsync(CancellationToken ct) =>
    ExecuteAsync("POST", "/api/v1/clients/enroll", null, Json.StartEnrollmentResponse, ct);

  public Task<OneOf<IReadOnlyList<DiscoveredCameraDto>, Error>> DiscoverAsync(
    DiscoveryRequest request, CancellationToken ct) =>
    ExecuteAsync("POST", "/api/v1/discovery",
      Serialize(request, Json.DiscoveryRequest), Json.IReadOnlyListDiscoveredCameraDto, ct);

  public Task<OneOf<IReadOnlyList<RecordingSegmentDto>, Error>> GetRecordingsAsync(
    Guid cameraId, ulong from, ulong to, string? profile, CancellationToken ct)
  {
    var path = BuildPath($"/api/v1/recordings/{cameraId}",
      ("profile", profile), ("from", from.ToString()), ("to", to.ToString()));
    return ExecuteAsync("GET", path, null, Json.IReadOnlyListRecordingSegmentDto, ct);
  }

  public Task<OneOf<TimelineResponse, Error>> GetTimelineAsync(
    Guid cameraId, ulong from, ulong to, string? profile, CancellationToken ct)
  {
    var path = BuildPath($"/api/v1/recordings/{cameraId}/timeline",
      ("profile", profile), ("from", from.ToString()), ("to", to.ToString()));
    return ExecuteAsync("GET", path, null, Json.TimelineResponse, ct);
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
    return ExecuteAsync("GET", path, null, Json.IReadOnlyListEventDto, ct);
  }

  public Task<OneOf<EventDto, Error>> GetEventAsync(Guid id, CancellationToken ct) =>
    ExecuteAsync("GET", $"/api/v1/events/{id}", null, Json.EventDto, ct);

  public Task<OneOf<RetentionPolicy, Error>> GetRetentionAsync(CancellationToken ct) =>
    ExecuteAsync("GET", "/api/v1/retention", null, Json.RetentionPolicy, ct);

  public Task<OneOf<Success, Error>> UpdateRetentionAsync(
    RetentionPolicy policy, CancellationToken ct) =>
    ExecuteVoidAsync("PUT", "/api/v1/retention",
      Serialize(policy, Json.RetentionPolicy), ct);

  public Task<OneOf<HealthResponse, Error>> GetHealthAsync(CancellationToken ct) =>
    ExecuteAsync("GET", "/api/v1/system/health", null, Json.HealthResponse, ct);

  public Task<OneOf<StorageResponse, Error>> GetStorageAsync(CancellationToken ct) =>
    ExecuteAsync("GET", "/api/v1/system/storage", null, Json.StorageResponse, ct);

  public Task<OneOf<ServerSettings, Error>> GetSettingsAsync(CancellationToken ct) =>
    ExecuteAsync("GET", "/api/v1/system/settings", null, Json.ServerSettings, ct);

  public Task<OneOf<Success, Error>> UpdateSettingsAsync(
    ServerSettings settings, CancellationToken ct) =>
    ExecuteVoidAsync("PUT", "/api/v1/system/settings",
      Serialize(settings, Json.ServerSettings), ct);

  public Task<OneOf<Success, Error>> GenerateCertsAsync(CancellationToken ct) =>
    ExecuteVoidAsync("POST", "/api/v1/system/certs", null, ct);

  public Task<OneOf<IReadOnlyList<PluginListItem>, Error>> GetPluginsAsync(
    string? type, CancellationToken ct)
  {
    var path = type != null ? $"/api/v1/plugins?type={Uri.EscapeDataString(type)}" : "/api/v1/plugins";
    return ExecuteAsync("GET", path, null, Json.IReadOnlyListPluginListItem, ct);
  }

  public Task<OneOf<PluginListItem, Error>> GetPluginAsync(string id, CancellationToken ct) =>
    ExecuteAsync("GET", $"/api/v1/plugins/{Uri.EscapeDataString(id)}", null, Json.PluginListItem, ct);

  public Task<OneOf<IReadOnlyList<SettingGroup>, Error>> GetPluginConfigSchemaAsync(
    string id, CancellationToken ct) =>
    ExecuteAsync("OPTIONS", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config",
      null, Json.IReadOnlyListSettingGroup, ct);

  public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetPluginConfigAsync(
    string id, CancellationToken ct) =>
    ExecuteAsync("GET", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config",
      null, Json.IReadOnlyDictionaryStringString, ct);

  public Task<OneOf<Success, Error>> UpdatePluginConfigAsync(
    string id, Dictionary<string, string> values, CancellationToken ct) =>
    ExecuteVoidAsync("PUT", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config",
      Serialize(values, Json.DictionaryStringString), ct);

  public Task<OneOf<Success, Error>> ValidatePluginFieldAsync(
    string id, ValidateFieldRequest request, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/config/validate",
      Serialize(request, Json.ValidateFieldRequest), ct);

  public Task<OneOf<Success, Error>> StartPluginAsync(string id, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/start", null, ct);

  public Task<OneOf<Success, Error>> StopPluginAsync(string id, CancellationToken ct) =>
    ExecuteVoidAsync("POST", $"/api/v1/plugins/{Uri.EscapeDataString(id)}/stop", null, ct);

  private async Task<OneOf<T, Error>> ExecuteAsync<T>(
    string method, string path, byte[]? body,
    JsonTypeInfo<T> responseTypeInfo, CancellationToken ct)
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

    var value = JsonSerializer.Deserialize(response.Body, responseTypeInfo);
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
    string method, string path, byte[]? body, CancellationToken ct)
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
    string method, string path, byte[]? body, CancellationToken ct)
  {
    var generationBefore = _tunnel.Generation;

    var request = new ApiRequestMessage
    {
      Method = method,
      Path = path,
      Body = body
    };

    var requestPayload = MessagePackSerializer.Serialize(request, ProtocolSerializer.Options);
    MuxStream stream;
    try
    {
      stream = await _tunnel.OpenStreamAsync(StreamTypes.ApiRequest, requestPayload, ct);
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogWarning("{Method} {Path} failed: {Message}", method, path, ex.Message);
      return Error.Create(ClientModuleIds.Api, 0x0006, Result.Unavailable, ex.Message);
    }

    await using (stream)
    {
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
  }

  private static byte[] Serialize<T>(T value, JsonTypeInfo<T> typeInfo) =>
    JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);

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
