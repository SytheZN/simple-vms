namespace Shared.Models;

public interface IClientRepository
{
  Task<OneOf<IReadOnlyList<Client>, Error>> GetAllAsync(CancellationToken ct = default);
  Task<OneOf<Client, Error>> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<Client, Error>> GetByCertificateSerialAsync(string serial, CancellationToken ct = default);
  Task<OneOf<Success, Error>> CreateAsync(Client client, CancellationToken ct = default);
  Task<OneOf<Success, Error>> UpdateAsync(Client client, CancellationToken ct = default);
}
