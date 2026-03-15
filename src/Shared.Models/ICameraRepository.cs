namespace Shared.Models;

public interface ICameraRepository
{
  Task<IReadOnlyList<Camera>> GetAllAsync(CancellationToken ct = default);
  Task<Camera?> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<Camera?> GetByAddressAsync(string address, CancellationToken ct = default);
  Task CreateAsync(Camera camera, CancellationToken ct = default);
  Task UpdateAsync(Camera camera, CancellationToken ct = default);
  Task DeleteAsync(Guid id, CancellationToken ct = default);
}
