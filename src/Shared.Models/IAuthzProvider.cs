namespace Shared.Models;

public interface IAuthzProvider
{
  Task<bool> AuthorizeAsync(string? identity, string operation, object? resource, CancellationToken ct);
  Task<IQueryable<T>> FilterAsync<T>(string? identity, IQueryable<T> query, CancellationToken ct);
}
