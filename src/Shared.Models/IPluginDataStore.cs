using System.Linq.Expressions;
namespace Shared.Models;

public interface IPluginDataStore
{
  Task<OneOf<T?, Error>> GetAsync<T>(string key, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>> GetAllAsync<T>(string? prefix = null, CancellationToken ct = default);
  Task<OneOf<Success, Error>> SetAsync<T>(string key, T value, CancellationToken ct = default);
  Task<OneOf<Success, Error>> DeleteAsync(string key, CancellationToken ct = default);
  Task<OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>> QueryAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
}
