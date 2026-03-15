namespace Shared.Models;

public interface ISettingsRepository
{
  Task<string?> GetAsync(string key, CancellationToken ct = default);
  Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
  Task SetAsync(string key, string value, CancellationToken ct = default);
  Task DeleteAsync(string key, CancellationToken ct = default);
}
