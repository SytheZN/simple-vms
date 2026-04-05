using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class DataStore : IDataStore
{
  private const ushort ModuleId = ModuleIds.PluginSqliteDataStore;
  private readonly ConnectionQueue _queue;
  private readonly string _pluginId;

  public DataStore(ConnectionQueue queue, string pluginId)
  {
    _queue = queue;
    _pluginId = pluginId;
  }

  public Task<OneOf<string?, Error>> GetAsync(string key, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<string?, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM plugin_data WHERE plugin_id = @pluginId AND key = @key";
        cmd.Parameters.AddWithValue("@pluginId", _pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to get plugin data '{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyList<KeyValuePair<string, string>>, Error>> GetAllAsync(string? prefix, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<KeyValuePair<string, string>>, Error>>(conn =>
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
        var results = new List<KeyValuePair<string, string>>();
        while (reader.Read())
        {
          var key = reader.GetString(0);
          var value = reader.GetString(1);
          results.Add(new KeyValuePair<string, string>(key, value));
        }
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0002, Result.InternalError, $"Failed to list plugin data: {ex.Message}");
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
          INSERT INTO plugin_data (plugin_id, key, value) VALUES (@pluginId, @key, @value)
          ON CONFLICT(plugin_id, key) DO UPDATE SET value = excluded.value
          """;
        cmd.Parameters.AddWithValue("@pluginId", _pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (Exception ex)
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
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0004, Result.InternalError, $"Failed to delete plugin data '{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyList<KeyValuePair<string, string>>, Error>> QueryAsync(Func<string, bool> predicate, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<KeyValuePair<string, string>>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM plugin_data WHERE plugin_id = @pluginId";
        cmd.Parameters.AddWithValue("@pluginId", _pluginId);

        using var reader = cmd.ExecuteReader();
        var results = new List<KeyValuePair<string, string>>();
        while (reader.Read())
        {
          var key = reader.GetString(0);
          var value = reader.GetString(1);
          if (predicate(value))
            results.Add(new KeyValuePair<string, string>(key, value));
        }
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to query plugin data: {ex.Message}");
      }
    }, ct);
  }
}
