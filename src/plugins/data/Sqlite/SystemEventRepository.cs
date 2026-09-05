using System.Text;
using Microsoft.Data.Sqlite;
namespace Data.Sqlite;

internal sealed class SystemEventRepository : ISystemEventRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteEvent;
  private readonly ConnectionQueue _queue;

  public SystemEventRepository(ConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<IReadOnlyList<SystemEvent>, Error>> QueryAsync(
    string? type, ulong from, ulong to, int limit, int offset, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<SystemEvent>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();

        var where = new StringBuilder("WHERE timestamp >= @from AND timestamp <= @to");
        cmd.Parameters.AddWithValue("@from", (long)from);
        cmd.Parameters.AddWithValue("@to", (long)to);

        if (type != null)
        {
          where.Append(" AND type = @type");
          cmd.Parameters.AddWithValue("@type", type);
        }

        cmd.CommandText = $"""
          SELECT * FROM system_events
          {where}
          ORDER BY timestamp DESC
          LIMIT @limit OFFSET @offset
          """;
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        using var reader = cmd.ExecuteReader();
        var results = new List<SystemEvent>();
        while (reader.Read())
          results.Add(ReadEvent(reader));
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0010, Result.InternalError,
          $"Failed to query system events: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<SystemEvent, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<SystemEvent, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM system_events WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0011, Result.NotFound, $"System event {id} not found");
        return ReadEvent(reader);
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0012, Result.InternalError,
          $"Failed to get system event {id}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> CreateAsync(SystemEvent evt, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO system_events (id, type, source, timestamp, metadata)
          VALUES (@id, @type, @source, @timestamp, @metadata)
          """;
        cmd.Parameters.AddWithValue("@id", evt.Id.ToString());
        cmd.Parameters.AddWithValue("@type", evt.Type);
        cmd.Parameters.AddWithValue("@source", evt.Source);
        cmd.Parameters.AddWithValue("@timestamp", (long)evt.Timestamp);
        cmd.Parameters.AddWithValue("@metadata",
          evt.Metadata != null ? (object)evt.Metadata.ToJson() : DBNull.Value);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
      {
        return Error.Create(ModuleId, 0x0013, Result.Conflict, $"System event {evt.Id} already exists");
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0014, Result.InternalError,
          $"Failed to create system event: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<int, Error>> DeleteOlderThanAsync(ulong cutoff, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<int, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM system_events WHERE timestamp < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", (long)cutoff);
        return cmd.ExecuteNonQuery();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0015, Result.InternalError,
          $"Failed to delete system events: {ex.Message}");
      }
    }, ct);
  }

  private static SystemEvent ReadEvent(SqliteDataReader reader)
  {
    var metadataOrdinal = reader.GetOrdinal("metadata");
    return new SystemEvent
    {
      Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
      Type = reader.GetString(reader.GetOrdinal("type")),
      Source = reader.GetString(reader.GetOrdinal("source")),
      Timestamp = (ulong)reader.GetInt64(reader.GetOrdinal("timestamp")),
      Metadata = reader.IsDBNull(metadataOrdinal)
        ? null : reader.GetString(metadataOrdinal).ToStringDictionaryOrNull()
    };
  }
}
