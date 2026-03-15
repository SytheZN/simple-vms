using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class SqlitePluginDataStore : IPluginDataStore
{
  private const ushort ModuleId = ModuleIds.PluginSqlitePluginData;
  private readonly SqliteConnectionQueue _queue;
  private readonly string _pluginId;

  public SqlitePluginDataStore(SqliteConnectionQueue queue, string pluginId)
  {
    _queue = queue;
    _pluginId = pluginId;
  }

  public Task<OneOf<T?, Error>> GetAsync<T>(string key, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<T?, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM plugin_data WHERE plugin_id = @pluginId AND key = @key";
        cmd.Parameters.AddWithValue("@pluginId", _pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        var result = cmd.ExecuteScalar() as string;
        return result != null ? JsonSerializer.Deserialize<T>(result) : default;
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to get plugin data '{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>> GetAllAsync<T>(string? prefix, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        if (prefix != null)
        {
          cmd.CommandText = """
            SELECT key, value FROM plugin_data
            WHERE plugin_id = @pluginId AND key LIKE @prefix
            """;
          cmd.Parameters.AddWithValue("@pluginId", _pluginId);
          cmd.Parameters.AddWithValue("@prefix", prefix + "%");
        }
        else
        {
          cmd.CommandText = "SELECT key, value FROM plugin_data WHERE plugin_id = @pluginId";
          cmd.Parameters.AddWithValue("@pluginId", _pluginId);
        }

        using var reader = cmd.ExecuteReader();
        var results = new List<KeyValuePair<string, T>>();
        while (reader.Read())
        {
          var key = reader.GetString(0);
          var value = JsonSerializer.Deserialize<T>(reader.GetString(1));
          if (value != null)
            results.Add(new KeyValuePair<string, T>(key, value));
        }
        return results;
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0002, Result.InternalError, $"Failed to list plugin data: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> SetAsync<T>(string key, T value, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO plugin_data (plugin_id, key, value) VALUES (@pluginId, @key, @value)
          ON CONFLICT(plugin_id, key) DO UPDATE SET value = excluded.value
          """;
        cmd.Parameters.AddWithValue("@pluginId", _pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", JsonSerializer.Serialize(value));
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to set plugin data '{key}': {ex.Message}");
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
        cmd.CommandText = "DELETE FROM plugin_data WHERE plugin_id = @pluginId AND key = @key";
        cmd.Parameters.AddWithValue("@pluginId", _pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0004, Result.InternalError, $"Failed to delete plugin data '{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>> QueryAsync<T>(
    Expression<Func<T, bool>> predicate, CancellationToken ct)
  {
    var compiled = predicate.Compile();
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM plugin_data WHERE plugin_id = @pluginId";
        cmd.Parameters.AddWithValue("@pluginId", _pluginId);

        using var reader = cmd.ExecuteReader();
        var results = new List<KeyValuePair<string, T>>();
        while (reader.Read())
        {
          var key = reader.GetString(0);
          var value = JsonSerializer.Deserialize<T>(reader.GetString(1));
          if (value != null && compiled(value))
            results.Add(new KeyValuePair<string, T>(key, value));
        }
        return results;
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to query plugin data: {ex.Message}");
      }
    }, ct);
  }
}
