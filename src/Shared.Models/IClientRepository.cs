namespace Shared.Models;

public interface IClientRepository
{
  Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken ct = default);
  Task<Client?> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<Client?> GetByCertificateSerialAsync(string serial, CancellationToken ct = default);
  Task CreateAsync(Client client, CancellationToken ct = default);
  Task UpdateAsync(Client client, CancellationToken ct = default);
}
