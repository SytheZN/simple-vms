-- Inserts one fake camera with a main and sub stream.
-- The sim:// scheme matches no ICaptureSource, so no pipeline is built and the
-- camera stays offline without connect attempts or reconnect backoff.
--
--   sqlite3 debug/data/server.db \
--     ".param set :camera_id 'f0000000-0000-4000-8000-000000000007'" \
--     ".read scripts/dev/insert-fake-camera.sql"
--
-- Omitting the param uses the default id below, so `sqlite3 db < script` works too.

CREATE TEMP TABLE fake_camera AS
SELECT coalesce(:camera_id, 'f0000000-0000-4000-8000-000000000001') AS id;

INSERT INTO cameras (
  id, name, address, provider_id, credentials, segment_duration,
  capabilities, config, retention_mode, retention_value, created_at, updated_at
) SELECT
  id,
  'Hikvision DS-2CD2143G2',
  'http://192.168.9.201/onvif/device_service',
  'onvif',
  NULL,
  NULL,
  '["events","analytics"]',
  json_object(
    'deviceUri', 'http://192.168.9.201/onvif/device_service',
    'manufacturer', 'Hikvision',
    'model', 'DS-2CD2143G2-I',
    'serialNumber', 'DS2CD2143G2I20240117AAWR000000001',
    'firmwareVersion', 'V5.7.3 build 220315',
    'mediaUri', 'http://192.168.9.201/onvif/media_service',
    'media2Uri', 'http://192.168.9.201/onvif/media2_service',
    'eventsUri', 'http://192.168.9.201/onvif/event_service'
  ),
  0,
  0,
  CAST(strftime('%s', 'now') AS INTEGER) * 1000000,
  CAST(strftime('%s', 'now') AS INTEGER) * 1000000
FROM fake_camera;

INSERT INTO streams (
  id, camera_id, profile, kind, format_id, codec, resolution, fps, bitrate,
  uri, recording_enabled, retention_mode, retention_value,
  parent_stream_id, producer_id, deleted_at
) SELECT
  lower(
    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4'
    || substr(hex(randomblob(2)), 2) || '-'
    || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2)
    || '-' || hex(randomblob(6))
  ),
  (SELECT id FROM fake_camera),
  profile, 0, 'fmp4', 'h264', resolution, fps, bitrate,
  'sim://192.168.9.201/Streaming/Channels/' || channel,
  recording_enabled, 0, 0, NULL, NULL, NULL
FROM (
  SELECT 'main' AS profile, '1920x1080' AS resolution, 30.0 AS fps,
         4096000 AS bitrate, '101' AS channel, 1 AS recording_enabled
  UNION ALL
  SELECT 'sub', '640x480', 15.0, 512000, '102', 0
);

DROP TABLE fake_camera;

-- Undo (streams cascade):
--   DELETE FROM cameras WHERE id = 'f0000000-0000-4000-8000-000000000001';
