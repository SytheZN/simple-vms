using Microsoft.Data.Sqlite;
using Shared.Models;

namespace Data.Sqlite;

public sealed class SqliteDataProvider : IDataProvider
{
  private const ushort ModuleId = ModuleIds.PluginSqliteMigration;
  private readonly SqliteConnectionQueue _queue;

  public string ProviderId => "sqlite";

  public ICameraRepository Cameras { get; }
  public IStreamRepository Streams { get; }
  public ISegmentRepository Segments { get; }
  public IKeyframeRepository Keyframes { get; }
  public IEventRepository Events { get; }
  public IClientRepository Clients { get; }
  public ISettingsRepository Settings { get; }

  public SqliteDataProvider(string databasePath)
  {
    var dir = Path.GetDirectoryName(databasePath);
    if (dir != null)
      Directory.CreateDirectory(dir);

    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = databasePath,
      Mode = SqliteOpenMode.ReadWriteCreate,
      Cache = SqliteCacheMode.Shared
    }.ToString();

    _queue = new SqliteConnectionQueue(connectionString);

    Cameras = new SqliteCameraRepository(_queue);
    Streams = new SqliteStreamRepository(_queue);
    Segments = new SqliteSegmentRepository(_queue);
    Keyframes = new SqliteKeyframeRepository(_queue);
    Events = new SqliteEventRepository(_queue);
    Clients = new SqliteClientRepository(_queue);
    Settings = new SqliteSettingsRepository(_queue);
  }

  public IPluginDataStore GetPluginStore(string pluginId)
  {
    return new SqlitePluginDataStore(_queue, pluginId);
  }

  public async Task<OneOf<Success, Error>> MigrateAsync(CancellationToken ct)
  {
    return await _queue.ExecuteAsync<OneOf<Success, Error>>(conn =>
    {
      try
      {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS cameras (
          id TEXT NOT NULL PRIMARY KEY,
          name TEXT NOT NULL,
          address TEXT NOT NULL,
          provider_id TEXT NOT NULL,
          credentials BLOB,
          segment_duration INTEGER,
          capabilities TEXT NOT NULL DEFAULT '[]',
          config TEXT NOT NULL DEFAULT '{}',
          retention_mode INTEGER NOT NULL DEFAULT 0,
          retention_value INTEGER NOT NULL DEFAULT 0,
          created_at INTEGER NOT NULL,
          updated_at INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_cameras_address ON cameras(address);

        CREATE TABLE IF NOT EXISTS streams (
          id TEXT NOT NULL PRIMARY KEY,
          camera_id TEXT NOT NULL REFERENCES cameras(id) ON DELETE CASCADE,
          profile TEXT NOT NULL,
          kind INTEGER NOT NULL DEFAULT 0,
          format_id TEXT NOT NULL,
          codec TEXT,
          resolution TEXT,
          fps INTEGER,
          bitrate INTEGER,
          uri TEXT NOT NULL,
          recording_enabled INTEGER NOT NULL DEFAULT 0,
          retention_mode INTEGER NOT NULL DEFAULT 0,
          retention_value INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_streams_camera_id ON streams(camera_id);

        CREATE TABLE IF NOT EXISTS segments (
          id TEXT NOT NULL PRIMARY KEY,
          stream_id TEXT NOT NULL REFERENCES streams(id) ON DELETE CASCADE,
          start_time INTEGER NOT NULL,
          end_time INTEGER NOT NULL,
          segment_ref TEXT NOT NULL,
          size_bytes INTEGER NOT NULL,
          keyframe_count INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_segments_stream_time ON segments(stream_id, start_time, end_time);

        CREATE TABLE IF NOT EXISTS keyframes (
          segment_id TEXT NOT NULL REFERENCES segments(id) ON DELETE CASCADE,
          timestamp INTEGER NOT NULL,
          byte_offset INTEGER NOT NULL,
          PRIMARY KEY (segment_id, timestamp)
        );

        CREATE TABLE IF NOT EXISTS events (
          id TEXT NOT NULL PRIMARY KEY,
          camera_id TEXT NOT NULL REFERENCES cameras(id) ON DELETE CASCADE,
          type TEXT NOT NULL,
          start_time INTEGER NOT NULL,
          end_time INTEGER,
          metadata TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_events_camera_time ON events(camera_id, start_time);
        CREATE INDEX IF NOT EXISTS idx_events_time ON events(start_time);
        CREATE INDEX IF NOT EXISTS idx_events_type_time ON events(type, start_time);

        CREATE TABLE IF NOT EXISTS clients (
          id TEXT NOT NULL PRIMARY KEY,
          name TEXT NOT NULL,
          certificate_serial TEXT NOT NULL,
          revoked INTEGER NOT NULL DEFAULT 0,
          enrolled_at INTEGER NOT NULL,
          last_seen_at INTEGER
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_clients_serial ON clients(certificate_serial);

        CREATE TABLE IF NOT EXISTS settings (
          key TEXT NOT NULL PRIMARY KEY,
          value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS plugin_data (
          plugin_id TEXT NOT NULL,
          key TEXT NOT NULL,
          value TEXT NOT NULL,
          PRIMARY KEY (plugin_id, key)
        );

        CREATE TABLE IF NOT EXISTS schema_version (
          version INTEGER NOT NULL
        );

        INSERT OR IGNORE INTO schema_version (rowid, version) VALUES (1, 1);
        """;
        cmd.ExecuteNonQuery();
        return new Success();
      }
      catch (SqliteException ex)
      {
        return Error.Create(ModuleId, 0x0001, Result.InternalError, $"Migration failed: {ex.Message}");
      }
    }, ct);
  }
}
