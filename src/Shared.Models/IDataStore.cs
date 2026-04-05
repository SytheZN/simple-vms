namespace Shared.Models;

public interface IDataStore
{
  Task<OneOf<string?, Error>> GetAsync(string key, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<KeyValuePair<string, string>>, Error>> GetAllAsync(string? prefix = null, CancellationToken ct = default);
  Task<OneOf<Success, Error>> SetAsync(string key, string value, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteAsync(string key, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<KeyValuePair<string, string>>, Error>> QueryAsync(Func<string, bool> predicate, CancellationToken ct = default);
}
