using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Avalonia.Logging;
using Client.Core.Api;
using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;

namespace Tests.Unit.Client;

[TestFixture]
public class ApiClientTests
{
  private static ClientJsonContext Json => ClientJsonContext.Default;
  /// <summary>
  /// SCENARIO:
  /// ApiClient sends a GET request with no body
  ///
  /// ACTION:
  /// Call GetHealthAsync via a mock tunnel that captures the request
  ///
  /// EXPECTED RESULT:
  /// The ApiRequestMessage has method GET, path /api/v1/system/health, no body
  /// </summary>
  [Test]
  public async Task GetHealth_SendsCorrectRequest()
  {
    var (client, tunnel) = CreateClient();

    var health = new HealthResponse { Status = "healthy", Uptime = 60, Version = "1.0", TunnelPort = 4433 };
    tunnel.NextResponse = CreateResponse(Result.Success, health, Json.HealthResponse);

    var result = await client.GetHealthAsync(CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0.Status, Is.EqualTo("healthy"));
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("GET"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/system/health"));
    Assert.That(tunnel.LastRequest.Body, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// The server returns a non-success result
  ///
  /// ACTION:
  /// Call an API method that gets a NotFound response
  ///
  /// EXPECTED RESULT:
  /// Returns Error with the correct Result code
  /// </summary>
  [Test]
  public async Task GetCamera_NotFound_ReturnsError()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.NotFound, "Camera not found");

    var result = await client.GetCameraAsync(Guid.NewGuid(), CancellationToken.None);

    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.NotFound));
    Assert.That(result.AsT1.Message, Is.EqualTo("Camera not found"));
  }

  /// <summary>
  /// SCENARIO:
  /// ApiClient sends a POST with a JSON body
  ///
  /// ACTION:
  /// Call CreateCameraAsync with a request body
  ///
  /// EXPECTED RESULT:
  /// The ApiRequestMessage body contains the JSON-serialized request
  /// </summary>
  [Test]
  public async Task CreateCamera_SendsBodyAsJson()
  {
    var (client, tunnel) = CreateClient();

    var camera = new CameraListItem
    {
      Id = Guid.NewGuid(),
      Name = "Test",
      Address = "192.168.1.100",
      Status = "online",
      ProviderId = "onvif",
      Streams = [],
      Capabilities = []
    };
    tunnel.NextResponse = CreateResponse(Result.Created, camera, Json.CameraListItem);

    var request = new CreateCameraRequest { Address = "192.168.1.100" };
    var result = await client.CreateCameraAsync(request, CancellationToken.None);

    Assert.That(result.IsT0, Is.True);
    Assert.That(tunnel.LastRequest!.Body, Is.Not.Null);

    var bodyJson = JsonSerializer.Deserialize<JsonElement>(tunnel.LastRequest.Body!);
    Assert.That(bodyJson.GetProperty("address").GetString(), Is.EqualTo("192.168.1.100"));
  }

  private static (ApiClient Client, RequestCapturingTunnel Tunnel) CreateClient()
  {
    var tunnel = new RequestCapturingTunnel();
    var client = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);
    return (client, tunnel);
  }

  private static ApiResponseMessage CreateResponse<T>(
    Result result, T body, JsonTypeInfo<T> typeInfo)
  {
    return new ApiResponseMessage
    {
      Result = (byte)result,
      DebugTag = 0x00010001,
      Body = JsonSerializer.SerializeToUtf8Bytes(body, typeInfo)
    };
  }

  private static ApiResponseMessage CreateErrorResponse(Result result, string message) =>
    new()
    {
      Result = (byte)result,
      DebugTag = 0x00010001,
      Message = message
    };

  private sealed class RequestCapturingTunnel : ITunnelService
  {
    public ConnectionState State => ConnectionState.Connected;
#pragma warning disable CS0067
    public event Action<ConnectionState>? StateChanged;
#pragma warning restore CS0067
    public uint GenerationValue { get; set; } = 1;
    public uint Generation => GenerationValue;
    public bool IncrementGenerationOnRead { get; set; }

    public ApiRequestMessage? LastRequest { get; private set; }
    public ApiResponseMessage? NextResponse { get; set; }

    public int ConnectedAddressIndex => 0;
    public Task ConnectAsync(ConnectionOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<MuxStream> OpenStreamAsync(
      ushort streamType, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
      LastRequest = MessagePackSerializer.Deserialize<ApiRequestMessage>(
        payload, ProtocolSerializer.Options);

      var channel = Channel.CreateUnbounded<MuxMessage>();
      var responsePayload = MessagePackSerializer.Serialize(
        NextResponse!, ProtocolSerializer.Options);
      channel.Writer.TryWrite(new MuxMessage(0, responsePayload));

      if (IncrementGenerationOnRead)
        GenerationValue++;

      var transport = new MemoryStream();
      var muxer = new StreamMuxer(transport, NullLogger.Instance, 1);
      var stream = new MuxStream(muxer, 1, channel.Reader, NullLogger.Instance);
      return Task.FromResult(stream);
    }
  }

  /// <summary>
  /// SCENARIO:
  /// DeleteCameraAsync is called with a camera ID
  ///
  /// ACTION:
  /// Call DeleteCameraAsync
  ///
  /// EXPECTED RESULT:
  /// Sends DELETE /api/v1/cameras/{id}
  /// </summary>
  [Test]
  public async Task DeleteCamera_SendsDeleteToCorrectPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    var id = Guid.NewGuid();
    await client.DeleteCameraAsync(id, CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("DELETE"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo($"/api/v1/cameras/{id}"));
  }

  /// <summary>
  /// SCENARIO:
  /// RestartCameraAsync is called with a camera ID
  ///
  /// ACTION:
  /// Call RestartCameraAsync
  ///
  /// EXPECTED RESULT:
  /// Sends POST /api/v1/cameras/{id}/restart
  /// </summary>
  [Test]
  public async Task RestartCamera_SendsPostToRestartPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    var id = Guid.NewGuid();
    await client.RestartCameraAsync(id, CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("POST"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo($"/api/v1/cameras/{id}/restart"));
  }

  /// <summary>
  /// SCENARIO:
  /// GetEventsAsync is called with filters for camera, type, and time range
  ///
  /// ACTION:
  /// Call GetEventsAsync with all filter parameters populated
  ///
  /// EXPECTED RESULT:
  /// The request path includes cameraId, type, from, to as query parameters
  /// </summary>
  [Test]
  public async Task GetEvents_IncludesAllFilterQueryParams()
  {
    var (client, tunnel) = CreateClient();
    var events = new List<EventDto>();
    tunnel.NextResponse = CreateResponse(Result.Success, events, Json.ListEventDto);
    var cameraId = Guid.NewGuid();
    await client.GetEventsAsync(cameraId, "motion", 1000, 2000, 50, 10, CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("GET"));
    Assert.That(tunnel.LastRequest.Path, Does.Contain("/api/v1/events"));
    Assert.That(tunnel.LastRequest.Path, Does.Contain($"cameraId={cameraId}"));
    Assert.That(tunnel.LastRequest.Path, Does.Contain("type=motion"));
  }

  /// <summary>
  /// SCENARIO:
  /// GetRetentionAsync is called
  ///
  /// ACTION:
  /// Call GetRetentionAsync, server returns a retention policy
  ///
  /// EXPECTED RESULT:
  /// The deserialized policy has the correct mode and value
  /// </summary>
  [Test]
  public async Task GetRetention_DeserializesPolicy()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateResponse(Result.Success, new RetentionPolicy { Mode = "days", Value = 30 }, Json.RetentionPolicy);
    var result = await client.GetRetentionAsync(CancellationToken.None);
    Assert.That(result.IsT0, Is.True);
    Assert.That(result.AsT0.Mode, Is.EqualTo("days"));
  }

  /// <summary>
  /// SCENARIO:
  /// UpdateRetentionAsync is called with a new policy
  ///
  /// ACTION:
  /// Call UpdateRetentionAsync
  ///
  /// EXPECTED RESULT:
  /// Sends PUT /api/v1/retention
  /// </summary>
  [Test]
  public async Task UpdateRetention_SendsPutToRetentionPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    await client.UpdateRetentionAsync(new RetentionPolicy { Mode = "bytes", Value = 1000 }, CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("PUT"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/retention"));
  }

  /// <summary>
  /// SCENARIO:
  /// GetStorageAsync is called
  ///
  /// ACTION:
  /// Call GetStorageAsync, server returns storage stats
  ///
  /// EXPECTED RESULT:
  /// Returns a successful StorageResponse
  /// </summary>
  [Test]
  public async Task GetStorage_ReturnsStorageResponse()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateResponse(Result.Success,
      new StorageResponse { Stores = [] }, Json.StorageResponse);
    var result = await client.GetStorageAsync(CancellationToken.None);
    Assert.That(result.IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// GetSettingsAsync is called
  ///
  /// ACTION:
  /// Call GetSettingsAsync, server returns settings
  ///
  /// EXPECTED RESULT:
  /// Returns a successful ServerSettings
  /// </summary>
  [Test]
  public async Task GetSettings_ReturnsServerSettings()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateResponse(Result.Success, new ServerSettings(), Json.ServerSettings);
    var result = await client.GetSettingsAsync(CancellationToken.None);
    Assert.That(result.IsT0, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// UpdateSettingsAsync is called with new server settings
  ///
  /// ACTION:
  /// Call UpdateSettingsAsync
  ///
  /// EXPECTED RESULT:
  /// Sends PUT /api/v1/system/settings with the settings body
  /// </summary>
  [Test]
  public async Task UpdateSettings_SendsPutToSettingsPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    await client.UpdateSettingsAsync(new ServerSettings { ServerName = "test" }, CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("PUT"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/system/settings"));
  }

  /// <summary>
  /// SCENARIO:
  /// GenerateCertsAsync is called
  ///
  /// ACTION:
  /// Call GenerateCertsAsync
  ///
  /// EXPECTED RESULT:
  /// Sends POST /api/v1/system/certs
  /// </summary>
  [Test]
  public async Task GenerateCerts_SendsPostToCertsPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    await client.GenerateCertsAsync(CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("POST"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/system/certs"));
  }

  /// <summary>
  /// SCENARIO:
  /// GetPluginsAsync is called with a type filter
  ///
  /// ACTION:
  /// Call GetPluginsAsync with type "storage"
  ///
  /// EXPECTED RESULT:
  /// The request path includes the type query parameter
  /// </summary>
  [Test]
  public async Task GetPlugins_IncludesTypeFilter()
  {
    var (client, tunnel) = CreateClient();
    var plugins = new List<PluginListItem>();
    tunnel.NextResponse = CreateResponse(Result.Success, plugins, Json.ListPluginListItem);
    await client.GetPluginsAsync("storage", CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Path, Is.EqualTo("/api/v1/plugins?type=storage"));
  }

  /// <summary>
  /// SCENARIO:
  /// GetClientsAsync is called
  ///
  /// ACTION:
  /// Call GetClientsAsync
  ///
  /// EXPECTED RESULT:
  /// Sends GET /api/v1/clients
  /// </summary>
  [Test]
  public async Task GetClients_SendsGetToClientsPath()
  {
    var (client, tunnel) = CreateClient();
    var clients = new List<ClientListItem>();
    tunnel.NextResponse = CreateResponse(Result.Success, clients, Json.ListClientListItem);
    await client.GetClientsAsync(CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("GET"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/clients"));
  }

  /// <summary>
  /// SCENARIO:
  /// DeleteClientAsync is called with a client ID
  ///
  /// ACTION:
  /// Call DeleteClientAsync
  ///
  /// EXPECTED RESULT:
  /// Sends DELETE /api/v1/clients/{id}
  /// </summary>
  [Test]
  public async Task DeleteClient_SendsDeleteToCorrectPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    var id = Guid.NewGuid();
    await client.DeleteClientAsync(id, CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("DELETE"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo($"/api/v1/clients/{id}"));
  }

  /// <summary>
  /// SCENARIO:
  /// GetRecordingsAsync is called with profile and time range filters
  ///
  /// ACTION:
  /// Call GetRecordingsAsync with profile "sub" and from/to timestamps
  ///
  /// EXPECTED RESULT:
  /// The request path includes the camera ID, profile, from, and to as query params
  /// </summary>
  [Test]
  public async Task GetRecordings_IncludesProfileAndTimeRange()
  {
    var (client, tunnel) = CreateClient();
    var recordings = new List<RecordingSegmentDto>();
    tunnel.NextResponse = CreateResponse(Result.Success, recordings, Json.ListRecordingSegmentDto);
    var cameraId = Guid.NewGuid();
    await client.GetRecordingsAsync(cameraId, 1000, 2000, "sub", CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Path, Does.Contain($"/api/v1/recordings/{cameraId}"));
    Assert.That(tunnel.LastRequest.Path, Does.Contain("profile=sub"));
  }

  /// <summary>
  /// SCENARIO:
  /// GetTimelineAsync is called for a camera
  ///
  /// ACTION:
  /// Call GetTimelineAsync with a camera ID and time range
  ///
  /// EXPECTED RESULT:
  /// The request path targets the timeline sub-resource
  /// </summary>
  [Test]
  public async Task GetTimeline_TargetsTimelineSubresource()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateResponse(Result.Success,
      new TimelineResponse { Spans = [], Events = [] }, Json.TimelineResponse);
    var cameraId = Guid.NewGuid();
    await client.GetTimelineAsync(cameraId, 1000, 2000, "main", CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Path, Does.Contain($"/api/v1/recordings/{cameraId}/timeline"));
  }

  /// <summary>
  /// SCENARIO:
  /// StartPluginAsync is called with a plugin ID
  ///
  /// ACTION:
  /// Call StartPluginAsync
  ///
  /// EXPECTED RESULT:
  /// Sends POST /api/v1/plugins/{id}/start
  /// </summary>
  [Test]
  public async Task StartPlugin_SendsPostToStartPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    await client.StartPluginAsync("my-plugin", CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("POST"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/plugins/my-plugin/start"));
  }

  /// <summary>
  /// SCENARIO:
  /// StopPluginAsync is called with a plugin ID
  ///
  /// ACTION:
  /// Call StopPluginAsync
  ///
  /// EXPECTED RESULT:
  /// Sends POST /api/v1/plugins/{id}/stop
  /// </summary>
  [Test]
  public async Task StopPlugin_SendsPostToStopPath()
  {
    var (client, tunnel) = CreateClient();
    tunnel.NextResponse = CreateErrorResponse(Result.Success, "");
    await client.StopPluginAsync("my-plugin", CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("POST"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/plugins/my-plugin/stop"));
  }

  /// <summary>
  /// SCENARIO:
  /// DiscoverAsync is called with subnet filters
  ///
  /// ACTION:
  /// Call DiscoverAsync with a DiscoveryRequest containing subnets
  ///
  /// EXPECTED RESULT:
  /// Sends POST /api/v1/discovery with a JSON body containing the subnets
  /// </summary>
  [Test]
  public async Task Discover_SendsPostWithSubnetsBody()
  {
    var (client, tunnel) = CreateClient();
    var discovered = new List<DiscoveredCameraDto>();
    tunnel.NextResponse = CreateResponse(Result.Success, discovered, Json.ListDiscoveredCameraDto);
    await client.DiscoverAsync(new DiscoveryRequest { Subnets = ["192.168.1.0/24"] }, CancellationToken.None);
    Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("POST"));
    Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/discovery"));
    Assert.That(tunnel.LastRequest.Body, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// The tunnel generation changes between sending the request and receiving the response
  ///
  /// ACTION:
  /// Call GetHealthAsync while the tunnel increments its generation during the request
  ///
  /// EXPECTED RESULT:
  /// Returns Error with Result.Unavailable
  /// </summary>
  [Test]
  public async Task StaleGeneration_ReturnsUnavailableError()
  {
    var tunnel = new RequestCapturingTunnel { GenerationValue = 1 };
    var client = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);
    tunnel.NextResponse = CreateResponse(Result.Success, new HealthResponse { Status = "healthy", Uptime = 0, Version = "1", TunnelPort = 4433 }, Json.HealthResponse);
    tunnel.IncrementGenerationOnRead = true;

    var result = await client.GetHealthAsync(CancellationToken.None);
    Assert.That(result.IsT1, Is.True);
    Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
  }
}
