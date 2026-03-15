using System.Linq.Expressions;

namespace Shared.Models;

public interface IPluginDataStore
{
  Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
  Task<IReadOnlyList<KeyValuePair<string, T>>> GetAllAsync<T>(string? prefix = null, CancellationToken ct = default);
  Task SetAsync<T>(string key, T value, CancellationToken ct = default);
  Task DeleteAsync(string key, CancellationToken ct = default);
  Task<IReadOnlyList<KeyValuePair<string, T>>> QueryAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
}
