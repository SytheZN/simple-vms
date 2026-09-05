using Shared.Models.Entities;

namespace Shared.Models;

public interface ISystemEventRepository
{
  Task<OneOf<IReadOnlyList<SystemEvent>, Error>> QueryAsync(string? type, ulong from, ulong to, int limit, int offset, CancellationToken ct = default);
  Task<OneOf<SystemEvent, Error>> GetByIdAsync(Guid id, CancellationToken ct = default);
  Task<OneOf<Success, Error>> CreateAsync(SystemEvent evt, CancellationToken ct = default);
  Task<OneOf<int, Error>> DeleteOlderThanAsync(ulong cutoff, CancellationToken ct = default);
}
