using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class SqliteSettingsRepository : ISettingsRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteSettings;
  private readonly SqliteConnectionQueue _queue;

  public SqliteSettingsRepository(SqliteConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<string?, Error>> GetAsync(string key, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<string?, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to get setting '{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyDictionary<string, string>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings";
        using var reader = cmd.ExecuteReader();
        var results = new Dictionary<string, string>();
        while (reader.Read())
          results[reader.GetString(0)] = reader.GetString(1);
        return results;
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0002, Result.InternalError, $"Failed to list settings: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> SetAsync(string key, string value, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO settings (key, value) VALUES (@key, @value)
          ON CONFLICT(key) DO UPDATE SET value = excluded.value
          """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to set setting '{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> DeleteAsync(string key, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0004, Result.InternalError, $"Failed to delete setting '{key}': {ex.Message}");
      }
    }, ct);
  }
}
