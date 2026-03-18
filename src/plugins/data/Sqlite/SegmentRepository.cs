using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class SegmentRepository : ISegmentRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteSegment;
  private readonly ConnectionQueue _queue;

  public SegmentRepository(ConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<IReadOnlyList<Segment>, Error>> GetByTimeRangeAsync(
    Guid streamId, ulong from, ulong to, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<Segment>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT * FROM segments
          WHERE stream_id = @streamId AND start_time <= @to AND end_time >= @from
          ORDER BY start_time
          """;
        cmd.Parameters.AddWithValue("@streamId", streamId.ToString());
        cmd.Parameters.AddWithValue("@from", (long)from);
        cmd.Parameters.AddWithValue("@to", (long)to);
        using var reader = cmd.ExecuteReader();
        var results = new List<Segment>();
        while (reader.Read())
          results.Add(ReadSegment(reader));
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to query segments by time range: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<IReadOnlyList<Segment>, Error>> GetOldestAsync(Guid streamId, int limit, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<Segment>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT * FROM segments
          WHERE stream_id = @streamId
          ORDER BY start_time ASC
          LIMIT @limit
          """;
        cmd.Parameters.AddWithValue("@streamId", streamId.ToString());
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        var results = new List<Segment>();
        while (reader.Read())
          results.Add(ReadSegment(reader));
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0002, Result.InternalError, $"Failed to query oldest segments: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<long, Error>> GetTotalSizeAsync(Guid streamId, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<long, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(size_bytes), 0) FROM segments WHERE stream_id = @streamId";
        cmd.Parameters.AddWithValue("@streamId", streamId.ToString());
        return (long)cmd.ExecuteScalar()!;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to get total size for stream {streamId}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> CreateAsync(Segment segment, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO segments (id, stream_id, start_time, end_time, segment_ref, size_bytes, keyframe_count)
          VALUES (@id, @streamId, @startTime, @endTime, @segmentRef, @sizeBytes, @keyframeCount)
          """;
        cmd.Parameters.AddWithValue("@id", segment.Id.ToString());
        cmd.Parameters.AddWithValue("@streamId", segment.StreamId.ToString());
        cmd.Parameters.AddWithValue("@startTime", (long)segment.StartTime);
        cmd.Parameters.AddWithValue("@endTime", (long)segment.EndTime);
        cmd.Parameters.AddWithValue("@segmentRef", segment.SegmentRef);
        cmd.Parameters.AddWithValue("@sizeBytes", segment.SizeBytes);
        cmd.Parameters.AddWithValue("@keyframeCount", segment.KeyframeCount);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
      {
        return Error.Create(ModuleId, 0x0004, Result.Conflict, $"Segment {segment.Id} already exists");
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to create segment: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> DeleteBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
  {
    if (ids.Count == 0)
      return Task.FromResult<OneOf<Success, Error>>(new Success());

    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        var parameters = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
          parameters[i] = $"@id{i}";
          cmd.Parameters.AddWithValue(parameters[i], ids[i].ToString());
        }
        cmd.CommandText = $"DELETE FROM segments WHERE id IN ({string.Join(", ", parameters)})";
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0006, Result.InternalError, $"Failed to delete segments: {ex.Message}");
      }
    }, ct);
  }

  private static Segment ReadSegment(SqliteDataReader reader)
  {
    return new Segment
    {
      Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
      StreamId = Guid.Parse(reader.GetString(reader.GetOrdinal("stream_id"))),
      StartTime = (ulong)reader.GetInt64(reader.GetOrdinal("start_time")),
      EndTime = (ulong)reader.GetInt64(reader.GetOrdinal("end_time")),
      SegmentRef = reader.GetString(reader.GetOrdinal("segment_ref")),
      SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
      KeyframeCount = reader.GetInt32(reader.GetOrdinal("keyframe_count"))
    };
  }
}
