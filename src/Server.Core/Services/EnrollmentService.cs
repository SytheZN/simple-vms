using System.Collections.Concurrent;
using System.Security.Cryptography;
using Server.Core;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

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

  public EnrollmentService(
    ICertificateService certs,
    IPluginHost plugins,
    ServerEndpoints endpoints)
  {
    _certs = certs;
    _plugins = plugins;
    _endpoints = endpoints;
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
      await Task.Delay(Timeout.Infinite, ct);
    }
    catch (OperationCanceledException) { }

    if (_pending.ContainsKey(token))
      state.StartGraceExpiry(TokenGracePeriod, () => _pending.TryRemove(token, out _));

    return new Success();
  }

  public async Task<OneOf<EnrollResponse, Error>> CompleteEnrollmentAsync(
    string token, CancellationToken ct)
  {
    if (!_pending.TryRemove(token, out var state))
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.Enrollment, 0x0001),
        "Invalid or expired enrollment token");

    state.CancelGraceExpiry();

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

    var client = new Client
    {
      Id = clientId,
      Name = $"Client {clientId.ToString()[..8]}",
      CertificateSerial = bundle.Serial,
      EnrolledAt = DateTimeOffset.UtcNow.ToUnixMicroseconds()
    };

    var result = await _plugins.DataProvider!.Clients.CreateAsync(client, ct);
    return result.Match<OneOf<EnrollResponse, Error>>(
      _ => response,
      error => error);
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
    private CancellationTokenSource? _graceCts;

    public void StartGraceExpiry(TimeSpan delay, Action onExpired)
    {
      CancelGraceExpiry();
      _graceCts = new CancellationTokenSource();
      var cts = _graceCts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(delay, cts.Token);
          onExpired();
        }
        catch (OperationCanceledException) { }
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
