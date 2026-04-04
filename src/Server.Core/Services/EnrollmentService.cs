using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Server.Plugins;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class EnrollmentService
{
  private static readonly char[] TokenChars =
    "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

  private readonly ConcurrentDictionary<string, ulong> _pending = new();
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
    _pending[token] = DateTimeOffset.UtcNow.ToUnixMicroseconds();

    var qrPayload = new QrPayload
    {
      V = 1,
      Addresses = _endpoints.HttpAddresses,
      Token = token
    };
    var qrData = JsonSerializer.Serialize(qrPayload, EnrollmentJsonContext.Default.QrPayload);

    return new StartEnrollmentResponse { Token = token, QrData = qrData };
  }

  public async Task<OneOf<EnrollResponse, Error>> CompleteEnrollmentAsync(
    string token, CancellationToken ct)
  {
    if (!_pending.TryRemove(token, out _))
      return new Error(
        Result.NotFound,
        new DebugTag(ModuleIds.Enrollment, 0x0001),
        "Invalid or expired enrollment token");

    var clientId = Guid.NewGuid();
    var bundle = _certs.GenerateClientCert(clientId);

    var tunnelAddresses = BuildTunnelAddresses();

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

  public void InvalidateToken(string token) =>
    _pending.TryRemove(token, out _);

  private string[] BuildTunnelAddresses()
  {
    var addresses = new List<string>();
    foreach (var httpAddr in _endpoints.HttpAddresses)
    {
      if (!Uri.TryCreate(httpAddr, UriKind.Absolute, out var uri))
        continue;
      addresses.Add($"{uri.Host}:{_endpoints.TunnelPort}");
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
}
