using Microsoft.Data.Sqlite;
using Shared.Models;

namespace Data.Sqlite;

internal sealed class CameraRepository : ICameraRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteCamera;
  private readonly ConnectionQueue _queue;

  public CameraRepository(ConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<IReadOnlyList<Camera>, Error>> GetAllAsync(CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<Camera>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM cameras";
        using var reader = cmd.ExecuteReader();
        var results = new List<Camera>();
        while (reader.Read())
          results.Add(ReadCamera(reader));
        return results;
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to list cameras: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Camera, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Camera, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM cameras WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0002, Result.NotFound, $"Camera {id} not found");
        return ReadCamera(reader);
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to get camera {id}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Camera, Error>> GetByAddressAsync(string address, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Camera, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM cameras WHERE address = @address";
        cmd.Parameters.AddWithValue("@address", address);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0004, Result.NotFound, $"Camera with address {address} not found");
        return ReadCamera(reader);
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to get camera by address {address}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> CreateAsync(Camera camera, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO cameras (id, name, address, provider_id, credentials, segment_duration,
            capabilities, config, retention_mode, retention_value, created_at, updated_at)
          VALUES (@id, @name, @address, @providerId, @credentials, @segmentDuration,
            @capabilities, @config, @retentionMode, @retentionValue, @createdAt, @updatedAt)
          """;
        BindCamera(cmd, camera);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
      {
        return Error.Create(ModuleId, 0x0006, Result.Conflict, $"Camera {camera.Id} already exists");
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0007, Result.InternalError, $"Failed to create camera: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> UpdateAsync(Camera camera, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          UPDATE cameras SET name = @name, address = @address, provider_id = @providerId,
            credentials = @credentials, segment_duration = @segmentDuration,
            capabilities = @capabilities, config = @config,
            retention_mode = @retentionMode, retention_value = @retentionValue,
            updated_at = @updatedAt
          WHERE id = @id
          """;
        BindCamera(cmd, camera);
        var rows = cmd.ExecuteNonQuery();
        if (rows == 0)
          return Error.Create(ModuleId, 0x0008, Result.NotFound, $"Camera {camera.Id} not found");
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0009, Result.InternalError, $"Failed to update camera: {ex.Message}");
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
        cmd.CommandText = "DELETE FROM cameras WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        var rows = cmd.ExecuteNonQuery();
        if (rows == 0)
          return Error.Create(ModuleId, 0x000A, Result.NotFound, $"Camera {id} not found");
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x000B, Result.InternalError, $"Failed to delete camera {id}: {ex.Message}");
      }
    }, ct);
  }

  private static void BindCamera(SqliteCommand cmd, Camera camera)
  {
    cmd.Parameters.AddWithValue("@id", camera.Id.ToString());
    cmd.Parameters.AddWithValue("@name", camera.Name);
    cmd.Parameters.AddWithValue("@address", camera.Address);
    cmd.Parameters.AddWithValue("@providerId", camera.ProviderId);
    cmd.Parameters.AddWithValue("@credentials", (object?)camera.Credentials ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@segmentDuration", (object?)camera.SegmentDuration ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@capabilities", camera.Capabilities.ToJson());
    cmd.Parameters.AddWithValue("@config", camera.Config.ToJson());
    cmd.Parameters.AddWithValue("@retentionMode", (int)camera.RetentionMode);
    cmd.Parameters.AddWithValue("@retentionValue", (long)camera.RetentionValue);
    cmd.Parameters.AddWithValue("@createdAt", (long)camera.CreatedAt);
    cmd.Parameters.AddWithValue("@updatedAt", (long)camera.UpdatedAt);
  }

  private static Camera ReadCamera(SqliteDataReader reader)
  {
    return new Camera
    {
      Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
      Name = reader.GetString(reader.GetOrdinal("name")),
      Address = reader.GetString(reader.GetOrdinal("address")),
      ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
      Credentials = reader.IsDBNull(reader.GetOrdinal("credentials"))
        ? null : (byte[])reader["credentials"],
      SegmentDuration = reader.IsDBNull(reader.GetOrdinal("segment_duration"))
        ? null : reader.GetInt32(reader.GetOrdinal("segment_duration")),
      Capabilities = reader.GetString(reader.GetOrdinal("capabilities")).ToStringArray(),
      Config = reader.GetString(reader.GetOrdinal("config")).ToStringDictionary(),
      RetentionMode = (RetentionMode)reader.GetInt32(reader.GetOrdinal("retention_mode")),
      RetentionValue = reader.GetInt64(reader.GetOrdinal("retention_value")),
      CreatedAt = (ulong)reader.GetInt64(reader.GetOrdinal("created_at")),
      UpdatedAt = (ulong)reader.GetInt64(reader.GetOrdinal("updated_at"))
    };
  }
}
