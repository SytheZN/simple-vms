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

  public Task<OneOf<Segment, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Segment, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM segments WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
          return ReadSegment(reader);
        return Error.Create(ModuleId, 0x000B, Result.NotFound, $"Segment {id} not found");
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x000C, Result.InternalError, $"Failed to get segment: {ex.Message}");
      }
    }, ct);
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

  public Task<OneOf<PlaybackPoint, Error>> FindPlaybackPointAsync(Guid streamId, ulong timestamp, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<PlaybackPoint, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT s.id, s.segment_ref, k.timestamp, k.byte_offset
          FROM segments s
          JOIN keyframes k ON k.segment_id = s.id
          WHERE s.stream_id = @streamId
            AND @ts BETWEEN s.start_time AND s.end_time
            AND k.timestamp <= @ts
          ORDER BY k.timestamp DESC
          LIMIT 1
          """;
        cmd.Parameters.AddWithValue("@streamId", streamId.ToString());
        cmd.Parameters.AddWithValue("@ts", (long)timestamp);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
          return ReadPlaybackPoint(reader);

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = """
          SELECT s.id, s.segment_ref, k.timestamp, k.byte_offset
          FROM segments s
          JOIN keyframes k ON k.segment_id = s.id
          WHERE s.stream_id = @streamId
            AND s.start_time > @ts
          ORDER BY s.start_time, k.timestamp
          LIMIT 1
          """;
        cmd2.Parameters.AddWithValue("@streamId", streamId.ToString());
        cmd2.Parameters.AddWithValue("@ts", (long)timestamp);
        using var reader2 = cmd2.ExecuteReader();
        if (reader2.Read())
          return ReadPlaybackPoint(reader2);

        return Error.Create(ModuleId, 0x0009, Result.NotFound, "No recording found at timestamp");
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x000A, Result.InternalError, $"Failed to find playback point: {ex.Message}");
      }
    }, ct);
  }

  private static PlaybackPoint ReadPlaybackPoint(SqliteDataReader reader)
  {
    return new PlaybackPoint
    {
      SegmentId = Guid.Parse(reader.GetString(0)),
      SegmentRef = reader.GetString(1),
      KeyframeTimestamp = (ulong)reader.GetInt64(2),
      ByteOffset = reader.GetInt64(3)
    };
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

  public Task<OneOf<IReadOnlyList<StreamStorageUsage>, Error>> GetSizeBreakdownAsync(CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<StreamStorageUsage>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT c.id AS camera_id, c.name AS camera_name, s.profile,
                 COALESCE(SUM(seg.size_bytes), 0) AS size_bytes,
                 COALESCE(SUM(seg.end_time - seg.start_time), 0) AS duration_micros
          FROM cameras c
          JOIN streams s ON s.camera_id = c.id
          LEFT JOIN segments seg ON seg.stream_id = s.id
          GROUP BY c.id, c.name, s.profile
          ORDER BY size_bytes DESC
          """;
        using var reader = cmd.ExecuteReader();
        var results = new List<StreamStorageUsage>();
        while (reader.Read())
        {
          results.Add(new StreamStorageUsage
          {
            CameraId = Guid.Parse(reader.GetString(reader.GetOrdinal("camera_id"))),
            CameraName = reader.GetString(reader.GetOrdinal("camera_name")),
            StreamProfile = reader.GetString(reader.GetOrdinal("profile")),
            SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
            DurationMicros = (ulong)reader.GetInt64(reader.GetOrdinal("duration_micros"))
          });
        }
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x000D, Result.InternalError,
          $"Failed to get size breakdown: {ex.Message}");
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

  public Task<OneOf<Success, Error>> UpdateAsync(Segment segment, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          UPDATE segments
          SET end_time = @endTime, size_bytes = @sizeBytes, keyframe_count = @keyframeCount
          WHERE id = @id
          """;
        cmd.Parameters.AddWithValue("@id", segment.Id.ToString());
        cmd.Parameters.AddWithValue("@endTime", (long)segment.EndTime);
        cmd.Parameters.AddWithValue("@sizeBytes", segment.SizeBytes);
        cmd.Parameters.AddWithValue("@keyframeCount", segment.KeyframeCount);
        var rows = cmd.ExecuteNonQuery();
        if (rows == 0)
          return Error.Create(ModuleId, 0x0007, Result.NotFound, $"Segment {segment.Id} not found");
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0008, Result.InternalError, $"Failed to update segment: {ex.Message}");
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
