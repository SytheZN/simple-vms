using System.Collections.Concurrent;
using System.Security.Cryptography;
using Server.Plugins;
using Shared.Models;
using Shared.Api;
using Shared.Models.Entities;
using Shared.Models.Events;

namespace Server.Core.Services;

public sealed class EnrollmentService
{
  private static readonly char[] TokenChars =
    "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

  private static readonly TimeSpan TokenGracePeriod = TimeSpan.FromSeconds(10);

  private readonly ConcurrentDictionary<string, TokenState> _pending = new();
  private readonly ICertificateService _certs;
  private readonly IPluginHost _plugins;
  private readonly ServerEndpoints _endpoints;
  private readonly IEventBus _eventBus;

  public EnrollmentService(
    ICertificateService certs,
    IPluginHost plugins,
    ServerEndpoints endpoints,
    IEventBus eventBus)
  {
    _certs = certs;
    _plugins = plugins;
    _endpoints = endpoints;
    _eventBus = eventBus;
  }

  public OneOf<StartEnrollmentResponse, Error> StartEnrollment()
  {
    var token = GenerateToken();

    var state = new TokenState();
    state.StartGraceExpiry(TokenGracePeriod, () => _pending.TryRemove(token, out _));
    _pending[token] = state;

    return new StartEnrollmentResponse { Token = token };
  }

  public async Task<OneOf<Success, Error>> HoldTokenAsync(string token, CancellationToken ct)
  {
    if (!_pending.TryGetValue(token, out var state))
      return Error.Create(ModuleIds.Enrollment, 0x0002, Result.NotFound,
        "Invalid or expired enrollment token");

    state.CancelGraceExpiry();

    try
    {
      await state.Consumed.WaitAsync(ct);
    }
    catch (OperationCanceledException) { }

    if (_pending.ContainsKey(token))
      state.StartGraceExpiry(TokenGracePeriod, () => _pending.TryRemove(token, out _));

    return new Success();
  }

  private const int MaxDeviceNameLength = 64;

  public async Task<OneOf<EnrollResponse, Error>> CompleteEnrollmentAsync(
    string token, string? deviceName, CancellationToken ct)
  {
    if (!_pending.TryRemove(token, out var state))
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.Enrollment, 0x0001),
        "Invalid or expired enrollment token");

    state.CancelGraceExpiry();
    state.MarkConsumed();

    var clientId = Guid.NewGuid();
    var bundle = _certs.GenerateClientCert(clientId);

    var tunnelAddresses = await BuildTunnelAddressesAsync(ct);

    var response = new EnrollResponse
    {
      Addresses = tunnelAddresses,
      Ca = _certs.RootCaPem,
      Cert = bundle.CertPem,
      Key = bundle.KeyPem,
      ClientId = clientId
    };

    var trimmedName = deviceName?.Trim() ?? "";
    if (trimmedName.Length > MaxDeviceNameLength)
      trimmedName = trimmedName[..MaxDeviceNameLength];

    var baseName = $"Client {clientId.ToString()[..8]}";
    var client = new Client
    {
      Id = clientId,
      Name = trimmedName.Length > 0 ? $"{baseName} {trimmedName}" : baseName,
      CertificateSerial = bundle.Serial,
      EnrolledAt = DateTimeOffset.UtcNow.ToUnixMicroseconds()
    };

    var result = await _plugins.DataProvider!.Clients.CreateAsync(client, ct);
    if (result.IsT1) return result.AsT1;

    await _eventBus.PublishAsync(new ClientEnrolled
    {
      ClientId = clientId,
      Name = client.Name,
      Timestamp = client.EnrolledAt
    }, ct);

    return response;
  }

  private async Task<string[]> BuildTunnelAddressesAsync(CancellationToken ct)
  {
    var addresses = new List<string>();
    var port = _endpoints.TunnelPort;

    var settings = await _plugins.DataProvider.Config.GetAllAsync("server", ct);
    if (settings.IsT0)
    {
      var map = settings.AsT0;

      var internalEndpoint = map.GetValueOrDefault("server.internalEndpoint");
      if (!string.IsNullOrWhiteSpace(internalEndpoint))
        addresses.Add(HostPort.NormalizeEndpoint(HostPort.ExtractHost(internalEndpoint), port));

      var externalHost = map.GetValueOrDefault("server.externalHost");
      var externalPortStr = map.GetValueOrDefault("server.externalPort");
      if (!string.IsNullOrWhiteSpace(externalHost)
          && int.TryParse(externalPortStr, out var externalPort))
        addresses.Add($"{externalHost}:{externalPort}");
    }

    return [.. addresses];
  }

  private static string GenerateToken()
  {
    Span<byte> bytes = stackalloc byte[8];
    RandomNumberGenerator.Fill(bytes);

    return string.Create(9, bytes.ToArray(), (span, b) =>
    {
      for (var i = 0; i < 4; i++)
        span[i] = TokenChars[b[i] % TokenChars.Length];
      span[4] = '-';
      for (var i = 0; i < 4; i++)
        span[5 + i] = TokenChars[b[4 + i] % TokenChars.Length];
    });
  }

  private sealed class TokenState
  {
    private readonly TaskCompletionSource _consumed =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _graceCts;

    public Task Consumed => _consumed.Task;

    public void MarkConsumed() => _consumed.TrySetResult();

    public void StartGraceExpiry(TimeSpan delay, Action onExpired)
    {
      CancelGraceExpiry();
      _graceCts = new CancellationTokenSource();
      var cts = _graceCts;
      _ = Task.Run(async () =>
      {
        await Task.Delay(delay, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!cts.IsCancellationRequested) onExpired();
      });
    }

    public void CancelGraceExpiry()
    {
      var prev = _graceCts;
      _graceCts = null;
      if (prev != null)
      {
        prev.Cancel();
        prev.Dispose();
      }
    }
  }
}
