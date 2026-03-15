namespace Shared.Models;

public interface ICameraRepository
{
  Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct = default);
  Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<Camera, Error>> GetByAddressAsync(string address, CancellationToken ct = default);
  Task<OneOf<Success, Error>> CreateAsync(Camera camera, CancellationToken ct = default);
  Task<OneOf<Success, Error>> UpdateAsync(Camera camera, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct = default);
}
