using Server.Plugins;
using Shared.Models;
using Shared.Api;
using Shared.Models.Entities;
using Shared.Models.Events;

namespace Server.Core.Services;

public sealed class ClientService
{
  private readonly IPluginHost _plugins;
  private readonly ConnectionTracker _connections;
  private readonly IEventBus _eventBus;

  public ClientService(IPluginHost plugins, ConnectionTracker connections, IEventBus eventBus)
  {
    _plugins = plugins;
    _connections = connections;
    _eventBus = eventBus;
  }

  public async Task<OneOf<IReadOnlyList<ClientDto>, Error>> GetAllAsync(
    CancellationToken ct)
  {
    var result = await _plugins.DataProvider!.Clients.GetAllAsync(ct);
    return result.Match<OneOf<IReadOnlyList<ClientDto>, Error>>(
      clients => clients.Select(c => ToDto(c)).ToList(),
      error => error);
  }

  public async Task<OneOf<ClientDto, Error>> GetByIdAsync(
    Guid id, CancellationToken ct)
  {
    var result = await _plugins.DataProvider!.Clients.GetByIdAsync(id, ct);
    return result.Match<OneOf<ClientDto, Error>>(
      client => ToDto(client),
      error => error);
  }

  public async Task<OneOf<Success, Error>> UpdateAsync(
    Guid id, UpdateClientRequest request, CancellationToken ct)
  {
    var result = await _plugins.DataProvider!.Clients.GetByIdAsync(id, ct);
    if (result.IsT1) return result.AsT1;

    var client = result.AsT0;
    var previousName = client.Name;
    client.Name = request.Name;
    var updateResult = await _plugins.DataProvider!.Clients.UpdateAsync(client, ct);
    if (updateResult.IsT1) return updateResult;

    await _eventBus.PublishAsync(new ClientRenamed
    {
      ClientId = id,
      PreviousName = previousName,
      Name = client.Name,
      Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
    }, ct);

    return updateResult;
  }

  public async Task<OneOf<Success, Error>> RevokeAsync(
    Guid id, CancellationToken ct)
  {
    var result = await _plugins.DataProvider!.Clients.GetByIdAsync(id, ct);
    if (result.IsT1) return result.AsT1;

    var client = result.AsT0;
    client.Revoked = true;
    _connections.Remove(id);
    var updateResult = await _plugins.DataProvider!.Clients.UpdateAsync(client, ct);
    if (updateResult.IsT1) return updateResult;

    await _eventBus.PublishAsync(new ClientRevoked
    {
      ClientId = id,
      Name = client.Name,
      Timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds()
    }, ct);

    return updateResult;
  }

  private ClientDto ToDto(Client c) =>
    new()
    {
      Id = c.Id,
      Name = c.Name,
      EnrolledAt = c.EnrolledAt,
      LastSeenAt = c.LastSeenAt,
      Connected = _connections.IsConnected(c.Id)
    };
}
