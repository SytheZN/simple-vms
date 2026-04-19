using System.Net.Http;
using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core;
using Server.Core.Routing;
using Server.Core.Services;
using Server.Core.PortForwarding;
using Server.Plugins;
using Server.Tunnel.Handlers;
using Shared.Models;
using Shared.Models.Dto;
using Shared.Protocol;
using Tests.Unit.Streaming;

namespace Tests.Unit.Tunnel;

[TestFixture]
public class ApiHandlerTests
{
  private ApiDispatcher _dispatcher = null!;
  private FakeServiceProvider _services = null!;

  [SetUp]
  public void SetUp()
  {
    _dispatcher = new ApiDispatcher();
    ApiRoutes.Register(_dispatcher);

    _services = new FakeServiceProvider();
    var health = new SystemHealth();
    health.TransitionToHealthy();
    var pluginHost = new SessionTestPluginHost(new StubDataProviderWithConfig());
    var endpoints = new ServerEndpoints { TunnelPort = 4433 };
    _services.Register(new SystemService(
      pluginHost, health, endpoints, new NoopPortForwardingApplier(), new StubHttpClientFactory(),
      NullLogger<SystemService>.Instance));
  }

  private sealed class NoopPortForwardingApplier : IPortForwardingApplier
  {
    public Task<OneOf<Success, Error>> ApplyAsync(CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
    public PortForwardingStatus GetStatus() => new() { Active = false };
  }

  private sealed class StubHttpClientFactory : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new();
  }

  private sealed class StubDataProviderWithConfig : IDataProvider
  {
    public string ProviderId => "test";
    public ICameraRepository Cameras => null!;
    public IStreamRepository Streams => null!;
    public ISegmentRepository Segments => null!;
    public IKeyframeRepository Keyframes => null!;
    public IEventRepository Events => null!;
    public IClientRepository Clients => null!;
    public IConfigRepository Config { get; } = new StubConfigRepository();
    public IDataStore GetDataStore(string pluginId) => null!;
  }

  private sealed class StubConfigRepository : IConfigRepository
  {
    public Task<OneOf<string?, Error>> GetAsync(
      string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<string?, Error>>((string?)null);

    public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(
      string pluginId, CancellationToken ct) =>
      Task.FromResult<OneOf<IReadOnlyDictionary<string, string>, Error>>(
        new Dictionary<string, string>
        {
          ["server.internalEndpoint"] = "192.168.1.50:4433"
        });

    public Task<OneOf<Success, Error>> SetAsync(
      string pluginId, string key, string value, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());

    public Task<OneOf<Success, Error>> DeleteAsync(
      string pluginId, string key, CancellationToken ct) =>
      Task.FromResult<OneOf<Success, Error>>(new Success());
  }

  /// <summary>
  /// SCENARIO:
  /// A valid GET /api/v1/system/health request
  ///
  /// ACTION:
  /// Run ApiHandler with the request delivered via channel
  ///
  /// EXPECTED RESULT:
  /// Response has Result.Success with a non-zero debug tag and a body
  /// </summary>
  [Test]
  public async Task HandleApi_ValidHealthRequest_ReturnsSuccessWithBody()
  {
    var response = await DispatchAsync("GET", "/api/v1/system/health");

    Assert.That((Result)response.Result, Is.EqualTo(Result.Success));
    Assert.That(response.DebugTag, Is.Not.EqualTo(0u));
    Assert.That(response.Body, Is.Not.Null.And.Not.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// A request for a path with no matching route
  ///
  /// ACTION:
  /// Run ApiHandler with GET /api/v1/nonexistent
  ///
  /// EXPECTED RESULT:
  /// Response has Result.NotFound and message containing the path
  /// </summary>
  [Test]
  public async Task HandleApi_UnknownRoute_ReturnsNotFound()
  {
    var response = await DispatchAsync("GET", "/api/v1/nonexistent");

    Assert.That((Result)response.Result, Is.EqualTo(Result.NotFound));
    Assert.That(response.Message, Does.Contain("/api/v1/nonexistent"));
  }

  /// <summary>
  /// SCENARIO:
  /// A request with a route parameter that does not match the :guid constraint
  ///
  /// ACTION:
  /// Run ApiHandler with GET /api/v1/cameras/not-a-guid
  ///
  /// EXPECTED RESULT:
  /// Response has Result.NotFound (route constraint rejects the match)
  /// </summary>
  [Test]
  public async Task HandleApi_InvalidGuidRouteParam_ReturnsNotFound()
  {
    var response = await DispatchAsync("GET", "/api/v1/cameras/not-a-guid");

    Assert.That((Result)response.Result, Is.EqualTo(Result.NotFound));
  }

  /// <summary>
  /// SCENARIO:
  /// A request with a query string parameter
  ///
  /// ACTION:
  /// Run ApiHandler with GET /api/v1/plugins?type=storage
  ///
  /// EXPECTED RESULT:
  /// Dispatcher parses the query string and the handler receives it
  /// </summary>
  [Test]
  public async Task HandleApi_WithQueryString_ParsedAndDispatched()
  {
    _services.Register(new PluginService(new SessionTestPluginHost()));

    var response = await DispatchAsync("GET", "/api/v1/plugins?type=storage");

    Assert.That((Result)response.Result, Is.EqualTo(Result.Success));
  }

  private async Task<ApiResponseMessage> DispatchAsync(
    string method, string path, byte[]? body = null)
  {
    var request = new ApiRequestMessage { Method = method, Path = path, Body = body };
    var requestPayload = MessagePackSerializer.Serialize(request, ProtocolSerializer.Options);

    var inputChannel = Channel.CreateUnbounded<MuxMessage>();
    await inputChannel.Writer.WriteAsync(new MuxMessage(0, requestPayload));
    inputChannel.Writer.Complete();

    ApiResponseMessage? response = null;

    await ApiHandler.RunAsync(
      inputChannel.Reader,
      (flags, payload, ct) =>
      {
        response = MessagePackSerializer.Deserialize<ApiResponseMessage>(
          payload, ProtocolSerializer.Options);
        return Task.CompletedTask;
      },
      _dispatcher, _services,
      NullLogger.Instance, CancellationToken.None);

    return response!;
  }

  private sealed class FakeServiceProvider : IServiceProvider
  {
    private readonly Dictionary<Type, object> _services = [];

    public void Register<T>(T service) where T : notnull =>
      _services[typeof(T)] = service;

    public object? GetService(Type serviceType) =>
      _services.GetValueOrDefault(serviceType);
  }
}
