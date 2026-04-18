using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Client.Core.Api;
using Client.Core.Tunnel;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;

namespace Tests.Unit.Client;

[TestFixture]
public class ApiClientExtraTests
{
  private static ClientJsonContext Json => ClientJsonContext.Default;

  /// <summary>
  /// SCENARIO:
  /// GetCameras with a status filter encodes it into the query string
  ///
  /// ACTION:
  /// Call GetCamerasAsync(status: "online")
  ///
  /// EXPECTED RESULT:
  /// Path is /api/v1/cameras?status=online and an empty list deserializes
  /// </summary>
  [Test]
  public async Task GetCameras_WithStatusFilter_AppendsQuery()
  {
    var (client, tunnel) = NewClient();
    tunnel.NextResponse = OkResponse(new List<CameraListItem>(), Json.IReadOnlyListCameraListItem);

    var result = await client.GetCamerasAsync("online", CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(result.IsT0, Is.True);
      Assert.That(tunnel.LastRequest!.Path, Is.EqualTo("/api/v1/cameras?status=online"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// GetCameras with no filter omits the query string
  ///
  /// ACTION:
  /// Call GetCamerasAsync(status: null)
  ///
  /// EXPECTED RESULT:
  /// Path is /api/v1/cameras with no '?'
  /// </summary>
  [Test]
  public async Task GetCameras_NoFilter_PlainPath()
  {
    var (client, tunnel) = NewClient();
    tunnel.NextResponse = OkResponse(new List<CameraListItem>(), Json.IReadOnlyListCameraListItem);

    await client.GetCamerasAsync(null, CancellationToken.None);

    Assert.That(tunnel.LastRequest!.Path, Is.EqualTo("/api/v1/cameras"));
  }

  /// <summary>
  /// SCENARIO:
  /// UpdateCamera serialises the request and PUTs to the camera path
  ///
  /// ACTION:
  /// Call UpdateCameraAsync with a name change
  ///
  /// EXPECTED RESULT:
  /// PUT /api/v1/cameras/{id} with the new name in the body
  /// </summary>
  [Test]
  public async Task UpdateCamera_PutsBodyToCameraPath()
  {
    var (client, tunnel) = NewClient();
    var id = Guid.NewGuid();
    tunnel.NextResponse = OkResponse(SampleCamera(id), Json.CameraListItem);

    await client.UpdateCameraAsync(id, new UpdateCameraRequest { Name = "Lobby" }, CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("PUT"));
      Assert.That(tunnel.LastRequest.Path, Is.EqualTo($"/api/v1/cameras/{id}"));
      var body = JsonSerializer.Deserialize<JsonElement>(tunnel.LastRequest.Body!);
      Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("Lobby"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// RefreshCamera POSTs to the refresh sub-resource
  ///
  /// ACTION:
  /// Call RefreshCameraAsync
  ///
  /// EXPECTED RESULT:
  /// POST /api/v1/cameras/{id}/refresh, no body
  /// </summary>
  [Test]
  public async Task RefreshCamera_PostsToRefreshSubresource()
  {
    var (client, tunnel) = NewClient();
    var id = Guid.NewGuid();
    tunnel.NextResponse = OkResponse(SampleCamera(id), Json.CameraListItem);

    await client.RefreshCameraAsync(id, CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("POST"));
      Assert.That(tunnel.LastRequest.Path, Is.EqualTo($"/api/v1/cameras/{id}/refresh"));
      Assert.That(tunnel.LastRequest.Body, Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// StartEnrollment POSTs to the enroll path with no body
  ///
  /// ACTION:
  /// Call StartEnrollmentAsync
  ///
  /// EXPECTED RESULT:
  /// POST /api/v1/clients/enroll, no body
  /// </summary>
  [Test]
  public async Task StartEnrollment_PostsWithNoBody()
  {
    var (client, tunnel) = NewClient();
    tunnel.NextResponse = OkResponse(
      new StartEnrollmentResponse { Token = "abcd" },
      Json.StartEnrollmentResponse);

    await client.StartEnrollmentAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("POST"));
      Assert.That(tunnel.LastRequest.Path, Is.EqualTo("/api/v1/clients/enroll"));
      Assert.That(tunnel.LastRequest.Body, Is.Null);
    });
  }

  /// <summary>
  /// SCENARIO:
  /// GetClient targets the singular client path
  ///
  /// ACTION:
  /// Call GetClientAsync
  ///
  /// EXPECTED RESULT:
  /// GET /api/v1/clients/{id}
  /// </summary>
  [Test]
  public async Task GetClient_GetsSingularClientPath()
  {
    var (client, tunnel) = NewClient();
    var id = Guid.NewGuid();
    tunnel.NextResponse = OkResponse(
      new ClientListItem { Id = id, Name = "C", EnrolledAt = 0, Connected = false },
      Json.ClientListItem);

    await client.GetClientAsync(id, CancellationToken.None);

    Assert.That(tunnel.LastRequest!.Path, Is.EqualTo($"/api/v1/clients/{id}"));
  }

  /// <summary>
  /// SCENARIO:
  /// UpdateClient PUTs the rename
  ///
  /// ACTION:
  /// Call UpdateClientAsync
  ///
  /// EXPECTED RESULT:
  /// PUT body carries the new name
  /// </summary>
  [Test]
  public async Task UpdateClient_PutsNameChange()
  {
    var (client, tunnel) = NewClient();
    var id = Guid.NewGuid();
    tunnel.NextResponse = OkResponse(
      new ClientListItem { Id = id, Name = "Renamed", EnrolledAt = 0, Connected = true },
      Json.ClientListItem);

    await client.UpdateClientAsync(id, new UpdateClientRequest { Name = "Renamed" },
      CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("PUT"));
      var body = JsonSerializer.Deserialize<JsonElement>(tunnel.LastRequest.Body!);
      Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("Renamed"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// GetEvent targets the singular event resource
  ///
  /// ACTION:
  /// Call GetEventAsync
  ///
  /// EXPECTED RESULT:
  /// GET /api/v1/events/{id}
  /// </summary>
  [Test]
  public async Task GetEvent_GetsSingularEventPath()
  {
    var (client, tunnel) = NewClient();
    var id = Guid.NewGuid();
    tunnel.NextResponse = OkResponse(
      new EventDto { Id = id, CameraId = Guid.NewGuid(), Type = "motion", StartTime = 0 },
      Json.EventDto);

    await client.GetEventAsync(id, CancellationToken.None);

    Assert.That(tunnel.LastRequest!.Path, Is.EqualTo($"/api/v1/events/{id}"));
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin config schema is requested via OPTIONS verb (the schema is the
  /// spec metadata, not the values)
  ///
  /// ACTION:
  /// Call GetPluginConfigSchemaAsync
  ///
  /// EXPECTED RESULT:
  /// OPTIONS /api/v1/plugins/{id}/config (URL-encoded id)
  /// </summary>
  [Test]
  public async Task GetPluginConfigSchema_UsesOptionsVerb()
  {
    var (client, tunnel) = NewClient();
    tunnel.NextResponse = OkResponse(
      (IReadOnlyList<SettingGroup>)new List<SettingGroup>(),
      Json.IReadOnlyListSettingGroup);

    await client.GetPluginConfigSchemaAsync("storage/filesystem", CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(tunnel.LastRequest!.Method, Is.EqualTo("OPTIONS"));
      Assert.That(tunnel.LastRequest.Path,
        Is.EqualTo("/api/v1/plugins/storage%2Ffilesystem/config"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// UpdatePluginConfig PUTs a dictionary body
  ///
  /// ACTION:
  /// Call UpdatePluginConfigAsync with two settings
  ///
  /// EXPECTED RESULT:
  /// Body is a JSON object containing both keys
  /// </summary>
  [Test]
  public async Task UpdatePluginConfig_PutsDictionaryBody()
  {
    var (client, tunnel) = NewClient();
    tunnel.NextResponse = SuccessResponse();

    await client.UpdatePluginConfigAsync("p",
      new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
      CancellationToken.None);

    var body = JsonSerializer.Deserialize<JsonElement>(tunnel.LastRequest!.Body!);
    Assert.Multiple(() =>
    {
      Assert.That(tunnel.LastRequest.Method, Is.EqualTo("PUT"));
      Assert.That(body.GetProperty("a").GetString(), Is.EqualTo("1"));
      Assert.That(body.GetProperty("b").GetString(), Is.EqualTo("2"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The server returns success but with no body where one was expected
  ///
  /// ACTION:
  /// Call a method whose typed response requires a body; the tunnel returns
  /// Success with Body = null
  ///
  /// EXPECTED RESULT:
  /// Returns Error with Result.InternalError - the client refuses to fabricate
  /// a default value
  /// </summary>
  [Test]
  public async Task TypedResponse_SuccessButNoBody_ReturnsInternalError()
  {
    var (client, tunnel) = NewClient();
    tunnel.NextResponse = new ApiResponseMessage
    {
      Result = (byte)Result.Success,
      DebugTag = 0x00010001,
      Body = null
    };

    var result = await client.GetHealthAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(result.IsT1, Is.True);
      Assert.That(result.AsT1.Result, Is.EqualTo(Result.InternalError));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The server's response body is malformed JSON for the expected type
  ///
  /// ACTION:
  /// Tunnel returns success with a body that is "null" literal (deserialises
  /// to a null reference)
  ///
  /// EXPECTED RESULT:
  /// Returns Error.InternalError flagging the deserialisation failure
  /// </summary>
  [Test]
  public async Task TypedResponse_NullBody_ReturnsInternalError()
  {
    var (client, tunnel) = NewClient();
    tunnel.NextResponse = new ApiResponseMessage
    {
      Result = (byte)Result.Success,
      DebugTag = 0x00010001,
      Body = "null"u8.ToArray()
    };

    var result = await client.GetHealthAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(result.IsT1, Is.True);
      Assert.That(result.AsT1.Result, Is.EqualTo(Result.InternalError));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The tunnel is unavailable - OpenStreamAsync throws InvalidOperationException
  ///
  /// ACTION:
  /// Call any API method against a tunnel that throws
  ///
  /// EXPECTED RESULT:
  /// Returns Error.Unavailable carrying the exception message
  /// </summary>
  [Test]
  public async Task TunnelUnavailable_ReturnsUnavailableError()
  {
    var tunnel = new ThrowingTunnel("not connected");
    var client = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);

    var result = await client.GetHealthAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
      Assert.That(result.IsT1, Is.True);
      Assert.That(result.AsT1.Result, Is.EqualTo(Result.Unavailable));
      Assert.That(result.AsT1.Message, Does.Contain("not connected"));
    });
  }

  private static (ApiClient Client, RecorderTunnel Tunnel) NewClient()
  {
    var tunnel = new RecorderTunnel();
    var client = new ApiClient(tunnel, NullLogger<ApiClient>.Instance);
    return (client, tunnel);
  }

  private static ApiResponseMessage OkResponse<T>(T value, JsonTypeInfo<T> typeInfo) =>
    new()
    {
      Result = (byte)Result.Success,
      DebugTag = 0x00010001,
      Body = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo)
    };

  private static ApiResponseMessage SuccessResponse() =>
    new() { Result = (byte)Result.Success, DebugTag = 0x00010001 };

  private static CameraListItem SampleCamera(Guid id) => new()
  {
    Id = id,
    Name = "Camera",
    Address = "192.168.1.1",
    Status = "online",
    ProviderId = "onvif",
    Streams = [],
    Capabilities = []
  };

  private sealed class RecorderTunnel : ITunnelService
  {
    public ConnectionState State => ConnectionState.Connected;
    public event Action<ConnectionState>? StateChanged;
    public uint Generation => 1;
    public int ConnectedAddressIndex => 0;

    public ApiRequestMessage? LastRequest { get; private set; }
    public ApiResponseMessage? NextResponse { get; set; }

    public Task ConnectAsync(ConnectionOptions options, CancellationToken ct)
    {
      StateChanged?.Invoke(State);
      return Task.CompletedTask;
    }
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<MuxStream> OpenStreamAsync(
      ushort streamType, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
      LastRequest = MessagePackSerializer.Deserialize<ApiRequestMessage>(
        payload, ProtocolSerializer.Options);

      var channel = Channel.CreateUnbounded<MuxMessage>();
      var responsePayload = MessagePackSerializer.Serialize(NextResponse!, ProtocolSerializer.Options);
      channel.Writer.TryWrite(new MuxMessage(0, responsePayload));

      var muxer = new StreamMuxer(new MemoryStream(), NullLogger.Instance, 1);
      var stream = new MuxStream(muxer, 1, channel.Reader, NullLogger.Instance);
      return Task.FromResult(stream);
    }
  }

  private sealed class ThrowingTunnel(string message) : ITunnelService
  {
    public ConnectionState State => ConnectionState.Disconnected;
    public event Action<ConnectionState>? StateChanged;
    public uint Generation => 1;
    public int ConnectedAddressIndex => -1;

    public Task ConnectAsync(ConnectionOptions options, CancellationToken ct)
    {
      StateChanged?.Invoke(State);
      return Task.CompletedTask;
    }
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<MuxStream> OpenStreamAsync(ushort t, ReadOnlyMemory<byte> p, CancellationToken ct) =>
      throw new InvalidOperationException(message);
  }
}
