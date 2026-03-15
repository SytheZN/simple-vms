# API

## Overview

The server exposes a single API surface used by both transport paths:

- **HTTP** - serves the web UI and enrollment endpoint on the local network
- **QUIC** - serves native clients (see [protocol.md](protocol.md) for transport details)

API operations are identical across both transports. The QUIC path uses the same method/path/body structure as HTTP (see protocol stream type `0x0200`). All responses use the standard response envelope (see [response-model.md](response-model.md)).

Request and response bodies are JSON over HTTP, MessagePack over QUIC.

All ID fields are `Guid` values, serialized as lowercase hyphenated strings (e.g. `550e8400-e29b-41d4-a716-446655440000`) in JSON and as binary in MessagePack.

## Authentication

- **HTTP**: Unauthenticated by default. Only accessible on the local network. Authentication can be added via the `IAuthProvider` plugin extension point (e.g. PIN, password, LDAP, SSO). When an auth provider is installed, it gates all HTTP endpoints except `/api/v1/enroll`.
- **QUIC**: Mutual TLS with client certificates. The client identity is derived from the certificate. This is always enforced and not pluggable.

### Authorization

Authorization is pluggable via `IAuthzProvider`. The provider receives an opaque identity string (QUIC client ID from certificate, or the identifier returned by the HTTP auth provider) and decides what is permitted. How identities map to accounts, roles, or permissions is entirely the provider's concern - the core system does not define accounts or roles.

When no `IAuthzProvider` plugin is installed, all operations are permitted - every identity is treated as `su` (unrestricted access). The built-in `IAuthzProvider` implements RBAC - it manages its own account and role data via `IPluginDataStore`, gates operations, and filters results based on role. Additional roles can be defined over time but the role set is not pre-defined.

The built-in provider can be replaced entirely (e.g. with an external policy engine) without changing the protocol or API surface. The authorization layer only filters; it never changes the shape of the API.

## Endpoints

### Enrollment

#### POST /api/v1/enroll

Exchange an enrollment token for client credentials. HTTP only.

See [enrollment.md](enrollment.md) for full details.

**Request:**

| Field | Type | Description |
|-------|------|-------------|
| `token` | string | Enrollment token (`XXXX-XXXX`) |

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `addresses` | string[] | QUIC addresses, ordered (local first) |
| `ca` | string | Root CA certificate (PEM) |
| `cert` | string | Client certificate (PEM) |
| `key` | string | Client private key (PEM) |
| `clientId` | Guid | Client identifier |

---

### Clients

#### POST /api/v1/clients/enroll

Start a pending enrollment. Generates a token and returns QR data for display in the web UI. The pending enrollment is held in memory and invalidated when the page session ends (see [enrollment.md](enrollment.md)).

**Request:** No body.

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `token` | string | Enrollment token |
| `qrData` | string | JSON string for QR code generation |

#### GET /api/v1/clients

List enrolled clients.

**Response body:** Array of:

| Field | Type | Description |
|-------|------|-------------|
| `id` | Guid | Client identifier |
| `name` | string | User-assigned name |
| `enrolledAt` | ulong | Unix microseconds |
| `lastSeenAt` | ulong? | Unix microseconds, last connection |
| `connected` | bool | Currently connected (runtime state, not persisted - derived from active QUIC connections) |

#### GET /api/v1/clients/{clientId}

Get a single client's details.

#### PUT /api/v1/clients/{clientId}

Update client metadata (e.g. name).

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Display name |

#### DELETE /api/v1/clients/{clientId}

Revoke a client. Immediately invalidates the client's certificate. The client record is retained in the database for history but excluded from API responses.

---

### Cameras

#### GET /api/v1/cameras

List all cameras.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `status` | string? | Filter by status: `online`, `offline`, `error` |

**Response body:** Array of:

| Field | Type | Description |
|-------|------|-------------|
| `id` | Guid | Camera identifier |
| `name` | string | Display name |
| `address` | string | Camera IP/hostname |
| `status` | string | `online`, `offline`, `error` (runtime state, not persisted - derived from active connections) |
| `providerId` | string | Camera provider plugin (e.g. `onvif`, `rtsp`) |
| `streams` | object[] | Available stream profiles (see below) |
| `capabilities` | string[] | Camera capabilities (e.g. `ptz`, `audio`, `events`) |

Stream profile object:

| Field | Type | Description |
|-------|------|-------------|
| `profile` | string | Profile name (e.g. `main`, `sub`) |
| `codec` | string | `h264` or `h265` |
| `resolution` | string | e.g. `1920x1080` |
| `fps` | int | Frames per second |
| `recordingEnabled` | bool | Whether this profile is being recorded |

#### POST /api/v1/cameras

Add a camera manually.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `address` | string | Camera IP/hostname |
| `providerId` | string? | Provider to use (default: auto-detect) |
| `credentials` | object? | `{ "username": "...", "password": "..." }` |
| `name` | string? | Display name (default: pulled from device) |

The server connects to the camera, pulls its configuration (profiles, capabilities), and begins streaming if successful.

#### GET /api/v1/cameras/{id}

Get full camera details including configuration.

#### PUT /api/v1/cameras/{id}

Update camera configuration.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `name` | string? | Display name |
| `credentials` | object? | Updated credentials |
| `streams` | object[]? | Per-stream config (recording enabled/disabled) |
| `segmentDuration` | int? | Target segment duration in seconds (null = use global default, actual duration rounds to the nearest keyframe boundary) |
| `retention` | object? | Retention policy override for this camera (see retention endpoint for shape) |

Retention override object:

| Field | Type | Description |
|-------|------|-------------|
| `mode` | string | `days`, `bytes`, or `percent` |
| `value` | number | Threshold value |

Omitting `retention` leaves the current policy unchanged. Setting it overrides the global default. To revert to the global default, delete the camera's retention override via a PUT with `retention` set to `null`.

#### DELETE /api/v1/cameras/{id}

Remove a camera. Stops streaming and recording. Recordings are retained according to retention policy (not deleted immediately).

#### POST /api/v1/cameras/{id}/restart

Restart the connection to a camera (disconnect and reconnect RTSP).

#### GET /api/v1/cameras/{id}/snapshot

Capture and return a current snapshot from the camera (JPEG).

**Response:** JPEG image bytes with `Content-Type: image/jpeg`.

---

### Discovery

#### POST /api/v1/discovery

Trigger camera discovery.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `subnets` | string[]? | CIDR ranges to scan (e.g. `["192.168.1.0/24"]`). Omit for local subnet WS-Discovery only. |
| `credentials` | object? | Default credentials to try during discovery |

**Response body:** Array of discovered cameras:

| Field | Type | Description |
|-------|------|-------------|
| `address` | string | Camera address |
| `name` | string? | Device name (if available) |
| `manufacturer` | string? | Device manufacturer |
| `model` | string? | Device model |
| `providerId` | string | Detected provider |
| `alreadyAdded` | bool | Whether this camera is already registered |

Discovery may take several seconds. The response is returned when the scan completes.

---

### Recordings

#### GET /api/v1/recordings/{cameraId}

Query available recordings for a camera. The server resolves the camera and profile to the corresponding stream in memory and performs a local lookup - there is no cross-service query.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `from` | ulong | Unix microseconds start time |
| `to` | ulong | Unix microseconds end time |
| `profile` | string? | Stream profile (default: `main`) |

**Response body:** Array of recording segments:

| Field | Type | Description |
|-------|------|-------------|
| `id` | Guid | Segment identifier |
| `startTime` | ulong | Unix microseconds segment start |
| `endTime` | ulong | Unix microseconds segment end |
| `profile` | string | Stream profile |
| `sizeBytes` | long | Segment file size |

#### GET /api/v1/recordings/{cameraId}/timeline

Get a condensed timeline of recording availability and events for a time range. Designed for rendering the client timeline scrubber.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `from` | ulong | Unix microseconds start time |
| `to` | ulong | Unix microseconds end time |
| `profile` | string? | Stream profile (default: `main`) |

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `spans` | object[] | Continuous recording spans |
| `events` | object[] | Events within the range |

Span object:

| Field | Type | Description |
|-------|------|-------------|
| `startTime` | ulong | Unix microseconds |
| `endTime` | ulong | Unix microseconds |

Event object:

| Field | Type | Description |
|-------|------|-------------|
| `id` | Guid | Event identifier |
| `type` | string | Event type |
| `startTime` | ulong | Unix microseconds |
| `endTime` | ulong? | Unix microseconds (null if instantaneous) |

---

### Events

#### GET /api/v1/events

Query events.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `cameraId` | Guid? | Filter by camera |
| `type` | string? | Filter by event type |
| `from` | ulong | Unix microseconds start time |
| `to` | ulong | Unix microseconds end time |
| `limit` | int? | Max results (default: 100) |
| `offset` | int? | Pagination offset |

**Response body:** Array of:

| Field | Type | Description |
|-------|------|-------------|
| `id` | Guid | Event identifier |
| `cameraId` | Guid | Source camera |
| `type` | string | Event type |
| `startTime` | ulong | Unix microseconds |
| `endTime` | ulong? | Unix microseconds |
| `metadata` | object? | Type-specific data |

#### GET /api/v1/events/{id}

Get a single event's full details.

---

### Retention

#### GET /api/v1/retention

Get the global default retention policy.

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `mode` | string | `days`, `bytes`, or `percent` |
| `value` | number | Threshold value |

#### PUT /api/v1/retention

Update the global default retention policy.

Per-camera overrides are set via `PUT /api/v1/cameras/{id}`.

---

### System

#### GET /api/v1/system/health

Server health check.

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | `healthy`, `degraded`, `unhealthy` |
| `uptime` | int | Seconds since server start (informational duration, not a timestamp) |
| `cameras` | object | `{ "total": N, "online": N, "offline": N, "error": N }` |
| `storage` | object | See storage endpoint |
| `version` | string | Server version |

#### GET /api/v1/system/storage

Storage status.

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `stores` | object[] | Per-storage-provider stats |

Store object:

| Field | Type | Description |
|-------|------|-------------|
| `totalBytes` | long | Total capacity (-1 if unknown) |
| `usedBytes` | long | Used space (-1 if unknown) |
| `freeBytes` | long | Free space (-1 if unknown) |
| `recordingBytes` | long | Space used by recordings (-1 if unknown) |

#### GET /api/v1/system/settings

Get server settings.

#### PUT /api/v1/system/settings

Update server settings.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `serverName` | string? | Display name for this server |
| `externalEndpoint` | string? | External hostname/IP for enrollment payloads |
| `segmentDuration` | int? | Default recording segment duration in seconds (default: 300). Actual duration rounds to the nearest keyframe boundary. |
| `discoverySubnets` | string[]? | Subnets to include in discovery scans |
| `defaultCredentials` | object? | Default camera credentials for discovery |

---

### Plugins

#### GET /api/v1/plugins

List installed plugins.

**Response body:** Array of:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Plugin identifier |
| `name` | string | Display name |
| `version` | string | Plugin version |
| `status` | string | `running`, `stopped`, `error` |
| `extensionPoints` | string[] | Which interfaces the plugin implements |

#### GET /api/v1/plugins/{id}

Get plugin details and configuration.

#### PUT /api/v1/plugins/{id}/config

Update plugin configuration. Schema is plugin-defined.

#### POST /api/v1/plugins/{id}/start

Start a stopped plugin.

#### POST /api/v1/plugins/{id}/stop

Stop a running plugin.

---

## Live Streams and Playback

Live video and playback are not REST operations - they use dedicated QUIC stream types (`0x0300` for live, `0x0301` for playback). See [protocol.md](protocol.md) for details.

For the web UI (HTTP only), live and playback are served via:

#### GET /api/v1/live/{cameraId}/{profile}

WebSocket upgrade. Server pushes fMP4 fragments for consumption via MSE (Media Source Extensions).

#### GET /api/v1/playback/{cameraId}/{profile}

WebSocket upgrade. Client sends seek commands, server pushes fMP4 fragments.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `from` | ulong | Unix microseconds start time |
| `to` | ulong? | Unix microseconds end time |
