# Data Model

## Overview

This document defines the entities, relationships, and query patterns that any `IDataProvider` implementation must support. It is database-engine agnostic - the provider chooses its own schema, storage format, and query language.

## Conventions

All timestamps are stored as **Unix microseconds** (`ulong`) - microseconds since 1970-01-01T00:00:00Z. This is consistent with the QUIC protocol timestamp format (see [protocol.md](protocol.md)) and avoids timezone/precision ambiguity across database engines.

## Entities

### Camera

Represents a registered camera.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `Name` | string | User-assigned display name |
| `Address` | string | Camera IP/hostname |
| `ProviderId` | string | Camera provider plugin that manages this camera |
| `Credentials` | encrypted blob | Camera username/password |
| `SegmentDuration` | int? | Recording segment duration in seconds (null = use server default) |
| `Capabilities` | string[] | Camera capabilities (e.g. `ptz`, `audio`, `events`) - populated by the camera provider during configuration |
| `Config` | map | Provider-specific configuration (opaque to core) |
| `RetentionMode` | enum | `Default`, `Days`, `Bytes`, `Percent` - fallback for streams that use `Default` |
| `RetentionValue` | long | Threshold value (ignored when mode is `Default`) |
| `CreatedAt` | ulong | Unix microseconds |
| `UpdatedAt` | ulong | Unix microseconds |

### Stream

Represents a stream profile on a camera. A camera has one or more streams. Streams are either quality profiles (video at different resolutions) or metadata profiles (motion data, etc.).

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `CameraId` | Guid | Foreign key > Camera |
| `Profile` | string | Profile name (e.g. `main`, `sub`, `motion`) |
| `Kind` | enum | `Quality` or `Metadata` |
| `FormatId` | string | `IStreamFormat` identifier (e.g. `fmp4`, `motion-grid`) |
| `Codec` | string? | Codec identifier (e.g. `h264`, `h265`); null for metadata profiles |
| `Resolution` | string? | e.g. `1920x1080`; null for metadata profiles |
| `Fps` | int? | Frames per second; null for metadata profiles |
| `Bitrate` | int? | Bitrate in kbps (if known) |
| `Uri` | string | Source URI |
| `RecordingEnabled` | bool | Whether this stream is being recorded |
| `RetentionMode` | enum | `Default`, `Days`, `Bytes`, `Percent` - `Default` inherits from Camera, which in turn inherits from global |
| `RetentionValue` | long | Threshold value (ignored when mode is `Default`) |

### Segment

Represents a recorded video segment file. The `SegmentRef` is an opaque string provided by the storage plugin - the data model does not interpret it.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `StreamId` | Guid | Foreign key > Stream |
| `StartTime` | ulong | Unix microseconds |
| `EndTime` | ulong | Unix microseconds |
| `SegmentRef` | string | Opaque reference from `IStorageProvider` |
| `SizeBytes` | long | Segment file size |
| `KeyframeCount` | int | Number of keyframes in this segment |

### Keyframe

Index of keyframe positions within a segment, enabling fast seek.

| Field | Type | Description |
|-------|------|-------------|
| `SegmentId` | Guid | Foreign key > Segment |
| `Timestamp` | ulong | Unix microseconds |
| `ByteOffset` | long | Byte offset within the segment file |

### Event

A camera event (motion, tamper, analytics, disconnect, etc.).

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `CameraId` | Guid | Foreign key > Camera |
| `Type` | string | Event type identifier |
| `StartTime` | ulong | Unix microseconds |
| `EndTime` | ulong? | Unix microseconds (null if instantaneous or ongoing) |
| `Metadata` | map? | Type-specific data (opaque to core) |

### Client

An enrolled client device.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key (same as `clientId` in enrollment) |
| `Name` | string | User-assigned display name |
| `CertificateSerial` | string | Serial number of the issued client certificate |
| `Revoked` | bool | Whether this client's access has been revoked |
| `EnrolledAt` | ulong | Unix microseconds |
| `LastSeenAt` | ulong? | Unix microseconds, last successful connection |

## Relationships

```mermaid
erDiagram
    Camera ||--o{ Stream : has
    Stream ||--o{ Segment : recorded_as
    Segment ||--o{ Keyframe : indexed_by
    Camera ||--o{ Event : produces
```

## Required Query Patterns

The `IDataProvider` exposes repository interfaces. Each repository must support the query patterns listed below. The provider translates these into whatever query mechanism its backing store supports.

All repository methods return `OneOf<T, Error>` for operations that produce a value, or `OneOf<Success, Error>` for mutations that do not. See [response-model.md](response-model.md) for the full error propagation model.

Anticipated failures (record not found, constraint violations, storage unavailable) are returned as `Error` values with a `Result` code, `DebugTag`, and message. Unanticipated failures (bugs, invariant violations) throw and are not caught.

### Error Contracts

Each repository documents which `Result` codes its operations can return. The common patterns:

| Result | When |
|--------|------|
| `NotFound` | A lookup by ID found no matching record |
| `Conflict` | An insert violated a uniqueness constraint (duplicate ID, duplicate address, etc.) |
| `InternalError` | A storage-layer failure (connection lost, corrupt data, I/O error) |

Providers may return additional codes where appropriate, but the above are the baseline. Collection queries (GetAll, GetByTimeRange, etc.) return an empty collection on no results, not `NotFound`.

### ICameraRepository

| Operation | Returns | Description |
|-----------|---------|-------------|
| `GetAll()` | `OneOf<IReadOnlyList<Camera>, Error>` | List all cameras |
| `GetById(id)` | `OneOf<Camera, Error>` | Get a single camera by ID. `NotFound` if absent. |
| `GetByAddress(address)` | `OneOf<Camera, Error>` | Find a camera by its address (for duplicate detection). `NotFound` if absent. |
| `Create(camera)` | `OneOf<Success, Error>` | Insert a new camera. `Conflict` if ID or address already exists. |
| `Update(camera)` | `OneOf<Success, Error>` | Update camera fields. `NotFound` if absent. |
| `Delete(id)` | `OneOf<Success, Error>` | Remove a camera and cascade to streams, segments, keyframes, events. `NotFound` if absent. |

### IStreamRepository

| Operation | Returns | Description |
|-----------|---------|-------------|
| `GetByCameraId(cameraId)` | `OneOf<IReadOnlyList<CameraStream>, Error>` | List all streams for a camera |
| `GetById(id)` | `OneOf<CameraStream, Error>` | Get a single stream. `NotFound` if absent. |
| `Upsert(stream)` | `OneOf<Success, Error>` | Create or update a stream (used during camera config sync) |
| `Delete(id)` | `OneOf<Success, Error>` | Remove a stream |

### ISegmentRepository

| Operation | Returns | Description |
|-----------|---------|-------------|
| `GetByTimeRange(streamId, from, to)` | `OneOf<IReadOnlyList<Segment>, Error>` | List segments overlapping a time range, ordered by start time |
| `GetOldest(streamId, limit)` | `OneOf<IReadOnlyList<Segment>, Error>` | Get the oldest N segments (for retention) |
| `GetTotalSize(streamId)` | `OneOf<long, Error>` | Sum of `SizeBytes` for all segments of a stream |
| `Create(segment)` | `OneOf<Success, Error>` | Insert a new segment |
| `DeleteBatch(ids)` | `OneOf<Success, Error>` | Remove segments by ID (batch, for retention) |

### IKeyframeRepository

| Operation | Returns | Description |
|-----------|---------|-------------|
| `GetBySegmentId(segmentId)` | `OneOf<IReadOnlyList<Keyframe>, Error>` | List all keyframes in a segment, ordered by timestamp |
| `GetNearest(segmentId, timestamp)` | `OneOf<Keyframe, Error>` | Find the keyframe at or before a timestamp (for seek). `NotFound` if no keyframe exists at or before the timestamp. |
| `CreateBatch(keyframes)` | `OneOf<Success, Error>` | Insert keyframes for a completed segment (batch) |
| `DeleteBySegmentIds(segmentIds)` | `OneOf<Success, Error>` | Remove keyframes when segments are purged |

### IEventRepository

| Operation | Returns | Description |
|-----------|---------|-------------|
| `Query(cameraId?, type?, from, to, limit, offset)` | `OneOf<IReadOnlyList<CameraEvent>, Error>` | Filtered, paginated event query |
| `GetById(id)` | `OneOf<CameraEvent, Error>` | Get a single event. `NotFound` if absent. |
| `Create(event)` | `OneOf<Success, Error>` | Insert an event |
| `GetByTimeRange(cameraId, from, to)` | `OneOf<IReadOnlyList<CameraEvent>, Error>` | Events within a range (for timeline overlay) |

### IClientRepository

| Operation | Returns | Description |
|-----------|---------|-------------|
| `GetAll()` | `OneOf<IReadOnlyList<Client>, Error>` | List all non-revoked clients |
| `GetById(id)` | `OneOf<Client, Error>` | Get a single non-revoked client. `NotFound` if absent or revoked. |
| `GetByCertificateSerial(serial)` | `OneOf<Client, Error>` | Look up client by cert serial, including revoked (used during TLS handshake to reject revoked certs). `NotFound` if absent. |
| `Create(client)` | `OneOf<Success, Error>` | Insert a new client |
| `Update(client)` | `OneOf<Success, Error>` | Update client fields (name, lastSeen, revoked) |

### ISettingsRepository

Key-value settings store for server-level configuration. Unlike entity repositories, `Get` returns `null` on a missing key (not `NotFound`) - checking whether a setting exists is a normal code path, not an error.

| Operation | Returns | Description |
|-----------|---------|-------------|
| `Get(key)` | `OneOf<string?, Error>` | Get a setting value. Returns null if the key does not exist. |
| `GetAll()` | `OneOf<IReadOnlyDictionary<string, string>, Error>` | Get all settings |
| `Set(key, value)` | `OneOf<Success, Error>` | Create or update a setting |
| `Delete(key)` | `OneOf<Success, Error>` | Remove a setting |

### IPluginDataStore

Generic key-value/document store for plugins to persist internal state (accounts, sessions, learned data, etc.) without bringing their own database. Every `IDataProvider` must implement this.

Each plugin gets an isolated namespace - a plugin can only access its own data.

Plugins that prefer to manage their own storage (separate database, files, etc.) are free to do so - they should expose the relevant paths or connection details as user-facing configuration via `IPluginConfig`.

Like `ISettingsRepository`, `Get` returns `null`/`default` on a missing key rather than `NotFound`.

| Operation | Returns | Description |
|-----------|---------|-------------|
| `Get<T>(key)` | `OneOf<T?, Error>` | Get a value by key, deserialized to `T`. Returns default if absent. |
| `GetAll<T>(prefix?)` | `OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>` | List all entries, optionally filtered by key prefix |
| `Set<T>(key, value)` | `OneOf<Success, Error>` | Create or update a value |
| `Delete(key)` | `OneOf<Success, Error>` | Remove a value |
| `Query<T>(predicate)` | `OneOf<IReadOnlyList<KeyValuePair<string, T>>, Error>` | Find entries matching a predicate (provider may support limited query expressiveness) |

The serialization format is provider-defined (JSON, MessagePack, etc.). Plugins work with typed objects; the provider handles serialization.

This is how, for example, the `IAuthzProvider` plugin stores accounts and role assignments - those are plugin concerns, not core data model entities.

## Performance Considerations

The following queries are performance-critical and providers should optimize for them:

- **`ISegmentRepository.GetByTimeRange`** - called on every playback seek and timeline render. Must be fast over large time ranges with many segments.
- **`IKeyframeRepository.GetNearest`** - called on every seek operation. Must return in sub-millisecond time for a typical segment (~60 keyframes per 5-minute segment at 1 GOP/2s).
- **`IEventRepository.GetByTimeRange`** - called on every timeline render. Must handle cameras with high event rates (continuous motion).
- **`ISegmentRepository.GetOldest`** and **`GetTotalSize`** - called by the retention engine on a regular interval. Should not block recording.

## Migration Contract

Each `IDataProvider` implements `MigrateAsync()` which is called on server startup and returns `OneOf<Success, Error>`. The provider is responsible for:

- Creating the schema on first run
- Migrating from older schema versions when the server is upgraded
- Making migrations safe to run concurrently (in case of multiple startup attempts)

The server does not dictate a migration framework - the provider uses whatever is appropriate for its backing store.
