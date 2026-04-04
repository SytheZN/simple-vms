using Microsoft.Extensions.Logging;
using Server.Plugins;
using Shared.Models;

namespace Server.Tunnel;

internal sealed class ClientValidator
{
  private readonly IPluginHost _plugins;
  private readonly ILogger _logger;

  public ClientValidator(IPluginHost plugins, ILogger logger)
  {
    _plugins = plugins;
    _logger = logger;
  }

  public async Task<OneOf<Guid, Error>> ValidateAsync(
    string certificateSerial, CancellationToken ct)
  {
    var clientResult = await _plugins.DataProvider.Clients
      .GetByCertificateSerialAsync(certificateSerial, ct);

    if (clientResult.IsT1)
    {
      _logger.LogDebug("Unknown client certificate serial: {Serial}", certificateSerial);
      return Error.Create(ModuleIds.Tunnel, 0x0010, Result.Unauthorized,
        "Unknown client certificate");
    }

    var client = clientResult.AsT0;
    if (client.Revoked)
    {
      _logger.LogInformation("Rejected revoked client {ClientId}", client.Id);
      return Error.Create(ModuleIds.Tunnel, 0x0011, Result.Forbidden,
        "Client has been revoked");
    }

    return client.Id;
  }
}
