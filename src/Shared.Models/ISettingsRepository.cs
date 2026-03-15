namespace Shared.Models;

public interface ISettingsRepository
{
  Task<OneOf<string?, Error>> GetAsync(string key, CancellationToken ct = default);
  Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(CancellationToken ct = default);
  Task<OneOf<Success, Error>> SetAsync(string key, string value, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteAsync(string key, CancellationToken ct = default);
}
