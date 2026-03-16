using Shared.Models;
using Shared.Models.Dto;

namespace Server.Core.Services;

public sealed class ClientService
{
  private readonly IDataProvider _data;
  private readonly ConnectionTracker _connections;

  public ClientService(IDataProvider data, ConnectionTracker connections)
  {
    _data = data;
    _connections = connections;
  }

  public async Task<OneOf<IReadOnlyList<ClientListItem>, Error>> GetAllAsync(
    CancellationToken ct)
  {
    var result = await _data.Clients.GetAllAsync(ct);
    return result.Match<OneOf<IReadOnlyList<ClientListItem>, Error>>(
      clients => clients.Select(c => ToDto(c)).ToList(),
      error => error);
  }

  public async Task<OneOf<ClientListItem, Error>> GetByIdAsync(
    Guid id, CancellationToken ct)
  {
    var result = await _data.Clients.GetByIdAsync(id, ct);
    return result.Match<OneOf<ClientListItem, Error>>(
      client => ToDto(client),
      error => error);
  }

  public async Task<OneOf<Success, Error>> UpdateAsync(
    Guid id, UpdateClientRequest request, CancellationToken ct)
  {
    var result = await _data.Clients.GetByIdAsync(id, ct);
    if (result.IsT1) return result.AsT1;

    var client = result.AsT0;
    client.Name = request.Name;
    return await _data.Clients.UpdateAsync(client, ct);
  }

  public async Task<OneOf<Success, Error>> RevokeAsync(
    Guid id, CancellationToken ct)
  {
    var result = await _data.Clients.GetByIdAsync(id, ct);
    if (result.IsT1) return result.AsT1;

    var client = result.AsT0;
    client.Revoked = true;
    _connections.Remove(id);
    return await _data.Clients.UpdateAsync(client, ct);
  }

  private ClientListItem ToDto(Client c) =>
    new()
    {
      Id = c.Id,
      Name = c.Name,
      EnrolledAt = c.EnrolledAt,
      LastSeenAt = c.LastSeenAt,
      Connected = _connections.IsConnected(c.Id)
    };
}
