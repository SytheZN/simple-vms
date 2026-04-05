using System.Text;
using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class EventRepository : IEventRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteEvent;
  private readonly ConnectionQueue _queue;

  public EventRepository(ConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<IReadOnlyList<CameraEvent>, Error>> QueryAsync(
    Guid? cameraId, string? type, ulong from, ulong to,
    int limit, int offset, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<CameraEvent>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();

        var where = new StringBuilder("WHERE start_time >= @from AND start_time <= @to");
        cmd.Parameters.AddWithValue("@from", (long)from);
        cmd.Parameters.AddWithValue("@to", (long)to);

        if (cameraId.HasValue)
        {
          where.Append(" AND camera_id = @cameraId");
          cmd.Parameters.AddWithValue("@cameraId", cameraId.Value.ToString());
        }
        if (type != null)
        {
          where.Append(" AND type = @type");
          cmd.Parameters.AddWithValue("@type", type);
        }

        cmd.CommandText = $"""
          SELECT * FROM events
          {where}
          ORDER BY start_time DESC
          LIMIT @limit OFFSET @offset
          """;
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        using var reader = cmd.ExecuteReader();
        var results = new List<CameraEvent>();
        while (reader.Read())
          results.Add(ReadEvent(reader));
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to query events: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<CameraEvent, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<CameraEvent, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM events WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0002, Result.NotFound, $"Event {id} not found");
        return ReadEvent(reader);
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to get event {id}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> CreateAsync(CameraEvent evt, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO events (id, camera_id, type, start_time, end_time, metadata)
          VALUES (@id, @cameraId, @type, @startTime, @endTime, @metadata)
          """;
        cmd.Parameters.AddWithValue("@id", evt.Id.ToString());
        cmd.Parameters.AddWithValue("@cameraId", evt.CameraId.ToString());
        cmd.Parameters.AddWithValue("@type", evt.Type);
        cmd.Parameters.AddWithValue("@startTime", (long)evt.StartTime);
        cmd.Parameters.AddWithValue("@endTime",
          evt.EndTime.HasValue ? (object)(long)evt.EndTime.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@metadata",
          evt.Metadata != null ? (object)evt.Metadata.ToJson() : DBNull.Value);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
      {
        return Error.Create(ModuleId, 0x0004, Result.Conflict, $"Event {evt.Id} already exists");
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to create event: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyList<CameraEvent>, Error>> GetByTimeRangeAsync(
    Guid cameraId, ulong from, ulong to, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<CameraEvent>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT * FROM events
          WHERE camera_id = @cameraId AND start_time >= @from AND start_time <= @to
          ORDER BY start_time
          """;
        cmd.Parameters.AddWithValue("@cameraId", cameraId.ToString());
        cmd.Parameters.AddWithValue("@from", (long)from);
        cmd.Parameters.AddWithValue("@to", (long)to);
        using var reader = cmd.ExecuteReader();
        var results = new List<CameraEvent>();
        while (reader.Read())
          results.Add(ReadEvent(reader));
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0006, Result.InternalError, $"Failed to query events by time range: {ex.Message}");
      }
    }, ct);
  }

  private static CameraEvent ReadEvent(SqliteDataReader reader)
  {
    var endTimeOrdinal = reader.GetOrdinal("end_time");
    var metadataOrdinal = reader.GetOrdinal("metadata");
    return new CameraEvent
    {
      Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
      CameraId = Guid.Parse(reader.GetString(reader.GetOrdinal("camera_id"))),
      Type = reader.GetString(reader.GetOrdinal("type")),
      StartTime = (ulong)reader.GetInt64(reader.GetOrdinal("start_time")),
      EndTime = reader.IsDBNull(endTimeOrdinal) ? null : (ulong)reader.GetInt64(endTimeOrdinal),
      Metadata = reader.IsDBNull(metadataOrdinal)
        ? null : reader.GetString(metadataOrdinal).ToStringDictionaryOrNull()
    };
  }
}
