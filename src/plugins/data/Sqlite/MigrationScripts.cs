using DbUp.Engine;

namespace Data.Sqlite;

internal static class MigrationScripts
{
  public static SqlScript[] All => [Initial, StreamsDerived];

  private static SqlScript Initial => new("0001_initial", """
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

    CREATE TABLE IF NOT EXISTS plugin_config (
      plugin_id TEXT NOT NULL,
      key TEXT NOT NULL,
      value TEXT NOT NULL,
      PRIMARY KEY (plugin_id, key)
    );

    CREATE TABLE IF NOT EXISTS plugin_data (
      plugin_id TEXT NOT NULL,
      key TEXT NOT NULL,
      value TEXT NOT NULL,
      PRIMARY KEY (plugin_id, key)
    );
    """);

  private static SqlScript StreamsDerived => new("0002_streams_derived", """
    CREATE TABLE streams_new (
      id TEXT NOT NULL PRIMARY KEY,
      camera_id TEXT NOT NULL REFERENCES cameras(id) ON DELETE CASCADE,
      profile TEXT NOT NULL,
      kind INTEGER NOT NULL DEFAULT 0,
      format_id TEXT NOT NULL,
      codec TEXT,
      resolution TEXT,
      fps REAL,
      bitrate INTEGER,
      uri TEXT,
      recording_enabled INTEGER NOT NULL DEFAULT 0,
      retention_mode INTEGER NOT NULL DEFAULT 0,
      retention_value INTEGER NOT NULL DEFAULT 0,
      parent_stream_id TEXT REFERENCES streams(id) ON DELETE CASCADE,
      producer_id TEXT,
      deleted_at INTEGER
    );

    INSERT INTO streams_new
      (id, camera_id, profile, kind, format_id, codec, resolution, fps, bitrate, uri,
       recording_enabled, retention_mode, retention_value)
    SELECT id, camera_id, profile, kind, format_id, codec, resolution, fps, bitrate, uri,
           recording_enabled, retention_mode, retention_value
    FROM streams;

    DROP TABLE streams;
    ALTER TABLE streams_new RENAME TO streams;

    CREATE INDEX idx_streams_camera_id ON streams(camera_id);
    CREATE INDEX idx_streams_parent ON streams(parent_stream_id);
    CREATE UNIQUE INDEX uk_streams_producer_active ON streams(camera_id, producer_id, profile) WHERE deleted_at IS NULL;
    """);
}
