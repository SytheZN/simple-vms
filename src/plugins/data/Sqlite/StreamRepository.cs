using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class StreamRepository : IStreamRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteStream;
  private readonly ConnectionQueue _queue;

  public StreamRepository(ConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<IReadOnlyList<CameraStream>, Error>> GetByCameraIdAsync(Guid cameraId, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<CameraStream>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM streams WHERE camera_id = @cameraId";
        cmd.Parameters.AddWithValue("@cameraId", cameraId.ToString());
        using var reader = cmd.ExecuteReader();
        var results = new List<CameraStream>();
        while (reader.Read())
          results.Add(ReadStream(reader));
        return results;
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to list streams for camera {cameraId}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<CameraStream, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<CameraStream, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM streams WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0002, Result.NotFound, $"Stream {id} not found");
        return ReadStream(reader);
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to get stream {id}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> UpsertAsync(CameraStream stream, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO streams (id, camera_id, profile, kind, format_id, codec, resolution,
            fps, bitrate, uri, recording_enabled, retention_mode, retention_value)
          VALUES (@id, @cameraId, @profile, @kind, @formatId, @codec, @resolution,
            @fps, @bitrate, @uri, @recordingEnabled, @retentionMode, @retentionValue)
          ON CONFLICT(id) DO UPDATE SET
            camera_id = excluded.camera_id, profile = excluded.profile,
            kind = excluded.kind, format_id = excluded.format_id,
            codec = excluded.codec, resolution = excluded.resolution,
            fps = excluded.fps, bitrate = excluded.bitrate,
            uri = excluded.uri, recording_enabled = excluded.recording_enabled,
            retention_mode = excluded.retention_mode, retention_value = excluded.retention_value
          """;
        BindStream(cmd, stream);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0004, Result.InternalError, $"Failed to upsert stream: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> DeleteAsync(Guid id, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM streams WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (Exception ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to delete stream {id}: {ex.Message}");
      }
    }, ct);
  }

  private static void BindStream(SqliteCommand cmd, CameraStream stream)
  {
    cmd.Parameters.AddWithValue("@id", stream.Id.ToString());
    cmd.Parameters.AddWithValue("@cameraId", stream.CameraId.ToString());
    cmd.Parameters.AddWithValue("@profile", stream.Profile);
    cmd.Parameters.AddWithValue("@kind", (int)stream.Kind);
    cmd.Parameters.AddWithValue("@formatId", stream.FormatId);
    cmd.Parameters.AddWithValue("@codec", (object?)stream.Codec ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@resolution", (object?)stream.Resolution ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@fps", (object?)stream.Fps ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@bitrate", (object?)stream.Bitrate ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@uri", stream.Uri);
    cmd.Parameters.AddWithValue("@recordingEnabled", stream.RecordingEnabled ? 1 : 0);
    cmd.Parameters.AddWithValue("@retentionMode", (int)stream.RetentionMode);
    cmd.Parameters.AddWithValue("@retentionValue", (long)stream.RetentionValue);
  }

  private static CameraStream ReadStream(SqliteDataReader reader)
  {
    return new CameraStream
    {
      Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
      CameraId = Guid.Parse(reader.GetString(reader.GetOrdinal("camera_id"))),
      Profile = reader.GetString(reader.GetOrdinal("profile")),
      Kind = (StreamKind)reader.GetInt32(reader.GetOrdinal("kind")),
      FormatId = reader.GetString(reader.GetOrdinal("format_id")),
      Codec = reader.IsDBNull(reader.GetOrdinal("codec"))
        ? null : reader.GetString(reader.GetOrdinal("codec")),
      Resolution = reader.IsDBNull(reader.GetOrdinal("resolution"))
        ? null : reader.GetString(reader.GetOrdinal("resolution")),
      Fps = reader.IsDBNull(reader.GetOrdinal("fps"))
        ? null : reader.GetInt32(reader.GetOrdinal("fps")),
      Bitrate = reader.IsDBNull(reader.GetOrdinal("bitrate"))
        ? null : reader.GetInt32(reader.GetOrdinal("bitrate")),
      Uri = reader.GetString(reader.GetOrdinal("uri")),
      RecordingEnabled = reader.GetInt32(reader.GetOrdinal("recording_enabled")) != 0,
      RetentionMode = (RetentionMode)reader.GetInt32(reader.GetOrdinal("retention_mode")),
      RetentionValue = reader.GetInt64(reader.GetOrdinal("retention_value"))
    };
  }
}
