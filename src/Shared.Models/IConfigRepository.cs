namespace Shared.Models;

public interface IConfigRepository
{
  Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct = default);
  Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(string pluginId, CancellationToken ct = default);
  Task<OneOf<Success, Error>> SetAsync(string pluginId, string key, string value, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteAsync(string pluginId, string key, CancellationToken ct = default);
}
