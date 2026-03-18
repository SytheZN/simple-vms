using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class ConfigRepository : IConfigRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteConfig;
  private readonly ConnectionQueue _queue;

  public ConfigRepository(ConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<string?, Error>> GetAsync(string pluginId, string key, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<string?, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM plugin_config WHERE plugin_id = @pluginId AND key = @key";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to get config '{pluginId}/{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyDictionary<string, string>, Error>> GetAllAsync(string pluginId, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyDictionary<string, string>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM plugin_config WHERE plugin_id = @pluginId";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        using var reader = cmd.ExecuteReader();
        var results = new Dictionary<string, string>();
        while (reader.Read())
          results[reader.GetString(0)] = reader.GetString(1);
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0002, Result.InternalError, $"Failed to list config for '{pluginId}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> SetAsync(string pluginId, string key, string value, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO plugin_config (plugin_id, key, value) VALUES (@pluginId, @key, @value)
          ON CONFLICT(plugin_id, key) DO UPDATE SET value = excluded.value
          """;
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to set config '{pluginId}/{key}': {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> DeleteAsync(string pluginId, string key, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM plugin_config WHERE plugin_id = @pluginId AND key = @key";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0004, Result.InternalError, $"Failed to delete config '{pluginId}/{key}': {ex.Message}");
      }
    }, ct);
  }
}
