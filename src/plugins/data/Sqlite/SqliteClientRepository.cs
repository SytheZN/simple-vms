using Microsoft.Data.Sqlite;
using Shared.Models;
namespace Data.Sqlite;

internal sealed class SqliteClientRepository : IClientRepository
{
  private const ushort ModuleId = ModuleIds.PluginSqliteClient;
  private readonly SqliteConnectionQueue _queue;

  public SqliteClientRepository(SqliteConnectionQueue queue)
  {
    _queue = queue;
  }

  public Task<OneOf<IReadOnlyList<Client>, Error>> GetAllAsync(CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<IReadOnlyList<Client>, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM clients WHERE revoked = 0";
        using var reader = cmd.ExecuteReader();
        var results = new List<Client>();
        while (reader.Read())
          results.Add(ReadClient(reader));
        return results;
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Failed to list clients: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Client, Error>> GetByIdAsync(Guid id, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Client, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM clients WHERE id = @id AND revoked = 0";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0002, Result.NotFound, $"Client {id} not found");
        return ReadClient(reader);
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0003, Result.InternalError, $"Failed to get client {id}: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Client, Error>> GetByCertificateSerialAsync(string serial, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Client, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM clients WHERE certificate_serial = @serial";
        cmd.Parameters.AddWithValue("@serial", serial);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
          return Error.Create(ModuleId, 0x0004, Result.NotFound, $"Client with serial {serial} not found");
        return ReadClient(reader);
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0005, Result.InternalError, $"Failed to get client by serial: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> CreateAsync(Client client, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          INSERT INTO clients (id, name, certificate_serial, revoked, enrolled_at, last_seen_at)
          VALUES (@id, @name, @serial, @revoked, @enrolledAt, @lastSeenAt)
          """;
        BindClient(cmd, client);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
      {
        return Error.Create(ModuleId, 0x0006, Result.Conflict, $"Client {client.Id} already exists");
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0007, Result.InternalError, $"Failed to create client: {ex.Message}");
      }
    }, ct);
  }

  public Task<OneOf<Success, Error>> UpdateAsync(Client client, CancellationToken ct)
  {
    return _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          UPDATE clients SET name = @name, certificate_serial = @serial,
            revoked = @revoked, last_seen_at = @lastSeenAt
          WHERE id = @id
          """;
        BindClient(cmd, client);
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0008, Result.InternalError, $"Failed to update client: {ex.Message}");
      }
    }, ct);
  }

  private static void BindClient(SqliteCommand cmd, Client client)
  {
    cmd.Parameters.AddWithValue("@id", client.Id.ToString());
    cmd.Parameters.AddWithValue("@name", client.Name);
    cmd.Parameters.AddWithValue("@serial", client.CertificateSerial);
    cmd.Parameters.AddWithValue("@revoked", client.Revoked ? 1 : 0);
    cmd.Parameters.AddWithValue("@enrolledAt", (long)client.EnrolledAt);
    cmd.Parameters.AddWithValue("@lastSeenAt",
      client.LastSeenAt.HasValue ? (object)(long)client.LastSeenAt.Value : DBNull.Value);
  }

  private static Client ReadClient(SqliteDataReader reader)
  {
    var lastSeenOrdinal = reader.GetOrdinal("last_seen_at");
    return new Client
    {
      Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
      Name = reader.GetString(reader.GetOrdinal("name")),
      CertificateSerial = reader.GetString(reader.GetOrdinal("certificate_serial")),
      Revoked = reader.GetInt32(reader.GetOrdinal("revoked")) != 0,
      EnrolledAt = (ulong)reader.GetInt64(reader.GetOrdinal("enrolled_at")),
      LastSeenAt = reader.IsDBNull(lastSeenOrdinal) ? null : (ulong)reader.GetInt64(lastSeenOrdinal)
    };
  }
}
