using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class KeyframeRepository : IKeyframeRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteKeyframe;
  private readonly ConnectionQueue _queue;

  public KeyframeRepository(ConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<IReadOnlyList<Keyframe>, Error>> GetBySegmentIdAsync(Guid segmentId, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<Keyframe>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT * FROM keyframes
          WHERE segment_id = @segmentId
          ORDER BY timestamp
          """;
        cmd.Parameters.AddWithValue("@segmentId", segmentId.ToString());
        using var reader = cmd.ExecuteReader();
        var results = new List<Keyframe>();
        while (reader.Read())
          results.Add(ReadKeyframe(reader));
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to list keyframes for segment {segmentId}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Keyframe, Error>> GetNearestAsync(Guid segmentId, ulong timestamp, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Keyframe, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT * FROM keyframes
          WHERE segment_id = @segmentId AND timestamp <= @timestamp
          ORDER BY timestamp DESC
          LIMIT 1
          """;
        cmd.Parameters.AddWithValue("@segmentId", segmentId.ToString());
        cmd.Parameters.AddWithValue("@timestamp", (long)timestamp);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0002, Result.NotFound, $"No keyframe at or before {timestamp} in segment {segmentId}");
        return ReadKeyframe(reader);
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to find nearest keyframe: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> CreateBatchAsync(IReadOnlyList<Keyframe> keyframes, CancellationToken ct)
  {
    if (keyframes.Count == 0)
      return Task.FromResult<OneOf<Success, Error>>(new Success());

    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var transaction = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO keyframes (segment_id, timestamp, byte_offset)
          VALUES (@segmentId, @timestamp, @byteOffset)
          """;

        var segmentIdParam = cmd.Parameters.Add("@segmentId", SqliteType.Text);
        var timestampParam = cmd.Parameters.Add("@timestamp", SqliteType.Integer);
        var byteOffsetParam = cmd.Parameters.Add("@byteOffset", SqliteType.Integer);

        foreach (var kf in keyframes)
        {
          segmentIdParam.Value = kf.SegmentId.ToString();
          timestampParam.Value = (long)kf.Timestamp;
          byteOffsetParam.Value = kf.ByteOffset;
          cmd.ExecuteNonQuery();
        }

        transaction.Commit();
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0004, Result.InternalError, $"Failed to create keyframes: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> DeleteBySegmentIdsAsync(IReadOnlyList<Guid> segmentIds, CancellationToken ct)
  {
    if (segmentIds.Count == 0)
      return Task.FromResult<OneOf<Success, Error>>(new Success());

    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        var parameters = new string[segmentIds.Count];
        for (var i = 0; i < segmentIds.Count; i++)
        {
          parameters[i] = $"@id{i}";
          cmd.Parameters.AddWithValue(parameters[i], segmentIds[i].ToString());
        }
        cmd.CommandText = $"DELETE FROM keyframes WHERE segment_id IN ({string.Join(", ", parameters)})";
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to delete keyframes: {ex.Message}");
      }
    }, ct);
  }

  private static Keyframe ReadKeyframe(SqliteDataReader reader)
  {
    return new Keyframe
    {
      SegmentId = Guid.Parse(reader.GetString(reader.GetOrdinal("segment_id"))),
      Timestamp = (ulong)reader.GetInt64(reader.GetOrdinal("timestamp")),
      ByteOffset = reader.GetInt64(reader.GetOrdinal("byte_offset"))
    };
  }
}
