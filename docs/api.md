# API

## Overview

The server exposes a single API surface used by both transport paths:

- **HTTP** - serves the web UI and enrollment endpoint on the local network
- **Tunnel** - serves native clients (see [protocol.md](protocol.md) for transport details)

API operations are identical across both transports. The tunnel path uses the same method/path/body structure as HTTP (see protocol stream type `0x0200`). All responses use the standard response envelope (see [response-model.md](response-model.md)).

Request and response bodies are JSON on both transports. On the tunnel path, the API framing envelope is MessagePack; only the `body` field carries JSON (see [protocol.md](protocol.md)).

All ID fields are `Guid` values, serialized as lowercase hyphenated strings (e.g. `550e8400-e29b-41d4-a716-446655440000`).

## Authentication

- **HTTP**: Unauthenticated by default. Only accessible on the local network. Authentication can be added via the `IAuthProvider` plugin extension point (e.g. PIN, password, LDAP, SSO). When an auth provider is installed, it gates all HTTP endpoints except `/api/v1/enroll`.
- **Tunnel**: Mutual TLS with client certificates. The client identity is derived from the certificate. This is always enforced and not pluggable.

### Authorization

Authorization is pluggable via `IAuthzProvider`. The provider receives an opaque identity string (tunnel client ID from certificate, or the identifier returned by the HTTP auth provider) and decides what is permitted. How identities map to accounts, roles, or permissions is entirely the provider's concern - the core system does not define accounts or roles.

When no `IAuthzProvider` plugin is installed, all operations are permitted. The authorization layer only filters; it never changes the shape of the API.

## Configuration Required

When the server is not ready to serve requests (no certs, plugins starting, data provider unreachable, or required settings missing), any API request outside the whitelist below returns HTTP 412 with body:

```
{ "error": "configuration-required", "reason": "...", "missing": [...] }
```

`reason` values:

| Reason | Meaning | Suggested client action |
|---|---|---|
| `missing-certs` | Setup wizard hasn't created certs yet. | Route to the setup wizard. |
| `starting` | Plugins are starting. | Wait briefly and retry health. |
| `data-provider-unavailable` | Data provider failed to start and a background retry is running. | Route to the setup wizard; it will render a storage-unavailable banner. |
| `missing-settings` | Required settings keys (listed in `missing`) are unset. | Route to the complete-settings form. |

Whitelist (always reachable regardless of state):
- `/api/v1/system/*`
- `/api/v1/plugins/*`

`missing` is only present when `reason` is `missing-settings`. It is an opaque list of identifiers naming settings the server is waiting on. Clients that recognise a key render the corresponding field; for anything unknown, the form should fall back to a generic prompt directing the admin to the Settings view. The set of keys is a property of the running server version, not part of this contract.

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
| `addresses` | string[] | Tunnel addresses, ordered (local first) |
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
| `connected` | bool | Currently connected (runtime state, not persisted - derived from active tunnel connections) |

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
| `address` | string | Camera address (normalized ONVIF URL) |
| `status` | string | `online`, `offline`, `error` (runtime state, not persisted - derived from active connections) |
| `providerId` | string | Camera provider plugin (e.g. `onvif`, `rtsp`) |
| `streams` | object[] | Available stream profiles (see below) |
| `capabilities` | string[] | Camera capabilities (e.g. `ptz`, `audio`, `events`, `analytics`) |

Configuration (segment duration, retention, per-stream recording, plugin-contributed fields) is exposed on the camera config endpoint; see [GET /api/v1/cameras/{id}/config](#get-apiv1camerasidconfig).

Stream profile object:

| Field | Type | Description |
|-------|------|-------------|
| `profile` | string | Profile name (e.g. `main`, `sub`, `motion-grid-sub`) |
| `kind` | string | `quality` or `metadata` |
| `codec` | string | e.g. `h264`, `h265`, `motion-grid` |
| `resolution` | string | e.g. `1920x1080` |
| `fps` | number | Frames per second (e.g. `30`, `0.25`) |

#### POST /api/v1/cameras

Add a camera.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `address` | string | Camera address. Accepts shorthand: `192.168.1.100`, `192.168.1.100:8080`, or full URL. The server normalizes to a complete ONVIF URL (prepends `http://`, appends `/onvif/device_service` if no path given). |
| `providerId` | string? | Provider to use (default: auto-detect) |
| `credentials` | object? | `{ "username": "...", "password": "..." }` |
| `name` | string? | Display name (default: pulled from device) |
| `rtspPortOverride` | int? | Override the RTSP port in stream URIs (for port-forwarded setups) |

The server connects to the camera, pulls its configuration (profiles, capabilities), and begins streaming if successful. Service URIs returned by the camera (media, events, RTSP streams) are rewritten to use the host and port from the provided address, so port-forwarded cameras work correctly.

#### POST /api/v1/cameras/probe

Probe a camera without persisting. Returns the camera's configuration (streams, capabilities, device info) for preview before adding.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `address` | string | Camera address (same normalization as POST /cameras) |
| `providerId` | string? | Provider to use |
| `credentials` | object? | `{ "username": "...", "password": "..." }` |

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Device name |
| `streams` | object[] | Discovered stream profiles |
| `capabilities` | string[] | Detected capabilities |
| `config` | object | Device info (serialNumber, firmwareVersion, service URIs) |

#### GET /api/v1/cameras/{id}

Get full camera details including configuration. Same shape as the list item.

#### PUT /api/v1/cameras/{id}

Update camera connection settings. Operational settings (segment duration, retention, per-stream recording, plugin-contributed fields) are managed via [PUT /api/v1/cameras/{id}/config](#put-apiv1camerasidconfig).

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `name` | string? | Display name |
| `address` | string? | Camera address (e.g. `http://192.168.1.50/onvif/device_service`). Normalised by the active provider on save. |
| `providerId` | string? | Reassign to a different camera provider plugin. The new provider's `ConfigureAsync` runs against the existing `address`/`credentials`; provider-specific config in the camera's `config` map may be reset. |
| `credentials` | object? | Updated credentials |
| `rtspPortOverride` | int? | Override RTSP port in stream URIs. Send `0` to clear an existing override; omit the field to leave it unchanged. |

Omitted fields are left unchanged. When `address`, `providerId`, `credentials`, or `rtspPortOverride` change, the server re-runs `ConfigureAsync` against the camera (a "refresh") so capability detection and stream URIs are updated, and publishes a `CameraConfigChanged` event. Streaming and recording pipelines reconcile dynamically without a server restart.

#### OPTIONS /api/v1/cameras/{id}/config

Return the schema for everything configurable about this camera and its streams. The schema combines built-in fields (segment duration, retention, per-stream recording, per-stream retention) and plugin-contributed fields from any plugin implementing `IPluginCameraSettings` or `IPluginStreamSettings`.

**Response body:**

```
{
  "camera": {
    "{pluginId}": [ SettingGroup, ... ]
  },
  "streams": {
    "{profile}": {
      "{pluginId}": [ SettingGroup, ... ]
    }
  }
}
```

Built-in host settings (segment duration, retention, per-stream recording) are surfaced under a reserved `core` plugin id so all sections follow the same dispatch path. A plugin entry is present only when its `GetSchema(...)` returns a non-empty schema for that scope. `SettingGroup` and `SettingField` follow the shapes defined in [plugins.md](plugins.md).

#### GET /api/v1/cameras/{id}/config

Return current values for the same shape returned by `OPTIONS`. Each `SettingGroup[]` array is flattened to a `{ "{fieldKey}": value, ... }` map keyed by `SettingField.Key`.

#### PUT /api/v1/cameras/{id}/config

Apply config changes. Partial updates are allowed; omitted sections and fields are left unchanged. The body matches the `GET` shape:

```
{
  "camera": {
    "{pluginId}": { "{fieldKey}": value, ... }
  },
  "streams": {
    "{profile}": {
      "{pluginId}": { "{fieldKey}": value, ... }
    }
  }
}
```

Each section is dispatched to the named plugin's `IPluginCameraSettings.ApplyValues` or `IPluginStreamSettings.ApplyValues`. A `CameraConfigChanged` event is published on changes that affect streaming or recording.

#### DELETE /api/v1/cameras/{id}

Remove a camera. Stops streaming and recording. Recordings are retained according to retention policy (not deleted immediately). Publishes a `CameraRemoved` event.

#### POST /api/v1/cameras/{id}/refresh

Re-probe the camera and update its configuration. Reconnects to the camera using stored credentials, refreshes capabilities, and merges stream profiles. Existing streams are updated in place (preserving recording toggles and retention settings). New streams (e.g. analytics metadata) are added. Publishes a `CameraConfigChanged` event.

#### POST /api/v1/cameras/{id}/restart

Restart the connection to a camera (disconnect and reconnect).

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
| `ports` | int[]? | Additional ONVIF ports to scan (default: 80, 8080, 8899). Not persisted. |
| `credentials` | object? | Credentials to try during discovery for device identification. Per-camera credentials are entered when adding. |

**Response body:** Array of discovered cameras:

| Field | Type | Description |
|-------|------|-------------|
| `address` | string | Camera address |
| `hostname` | string? | Reverse DNS hostname (if PTR record exists) |
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

Per-camera overrides are set via `PUT /api/v1/cameras/{id}/config`.

---

### System

#### GET /api/v1/system/health

Server health check.

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | `healthy`, `degraded`, `unhealthy`, `missing-certs`, `starting` |
| `uptime` | int | Seconds since server start (informational duration, not a timestamp) |
| `version` | string | Server version |
| `tunnelPort` | int | TCP port the server listens on for native client tunnel connections |
| `httpPort` | int | TCP port the server listens on for the web UI and HTTP enrollment API |
| `missingSettings` | string[]? | Non-empty when required settings are unset. See [Configuration Required](#configuration-required). |

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

Get server settings. Response carries the same shape as the PUT body.

#### PUT /api/v1/system/settings

Update server settings. Partial updates are allowed; omitted fields are left unchanged.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `serverName` | string? | Display name for this server |
| `internalEndpoint` | string? | LAN address other devices use to reach the server. Accepts `host`, `host:port`, or a full `http(s)://host[:port]` URL. Rejected for loopback, link-local, `localhost`, or `host.docker.internal`. |
| `mode` | string? | Remote access mode: `none`, `manual`, or `upnp`. Required when any remote-access field is set. |
| `externalHost` | string? | External hostname or IP placed in enrollment payloads. Required in `manual` and `upnp`; rejected in `none`. |
| `externalPort` | int? | External TCP port. 1-65535 in `manual`; 20000-60000 in `upnp`; rejected in `none`. |
| `upnpRouterAddress` | string? | Router's LAN address, IPv4 literal or hostname. Required in `upnp`; rejected in `manual` and `none`. |
| `segmentDuration` | int? | Default recording segment duration in seconds (default: 300). Actual duration rounds to the nearest sync point boundary. |
| `discoverySubnets` | string[]? | Subnets to include in discovery scans |

When `mode` is `upnp`, save attempts the port mapping synchronously, trying NAT-PMP first and falling back to UPnP IGD if NAT-PMP does not respond. A router-reported fault is returned as `result: badRequest` with the fault description in `message`; the configured values are still persisted so the admin can correct and retry. A background reconcile refreshes the mapping's lease every 60 seconds.

#### GET /api/v1/system/verify-remote-address

Look up the server's public IP (via `api.ipify.org`) and optionally resolve a hostname via the server host's DNS resolver. Used by the settings UI to confirm an external host is reachable from the public internet. The client compares the returned values.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `host` | string? | Hostname or IP literal to resolve. Omit to return public IP only. IP literals are echoed back unchanged. |

**Response body:**

| Field | Type | Description |
|-------|------|-------------|
| `publicIp` | string | Public IP as reported by `api.ipify.org` |
| `resolvedIps` | string[]? | IPv4 addresses the server's resolver returned for `host` |

Returns `result: unavailable` when ipify is unreachable or DNS resolution fails; the message carries the underlying reason.

#### POST /api/v1/system/certs

Generate root CA and server certificate. On a new installation this is called from the setup wizard to create the initial certificates. On an existing installation this regenerates the root CA, permanently disconnecting all enrolled clients (their certificates become invalid).

**Request:** No body.

**Response body:** No body. The server begins startup continuation (schema migration, plugin loading) once the certificates are written.

---

### Plugins

#### GET /api/v1/plugins\[?type={extensionPoint}\]

List discovered plugins, optionally filtered by extension point type (e.g. `data-provider`, `capture-source`, `camera-provider`). Available during setup before plugins are started.

**Response body:** Array of:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Plugin identifier |
| `name` | string | Display name |
| `description` | string? | Plugin description |
| `version` | string | Plugin version |
| `status` | string | `discovered`, `running`, `stopped`, `error` |
| `extensionPoints` | string[] | Which interfaces the plugin implements |
| `userStartable` | bool | Whether the plugin supports user-initiated start/stop |
| `hasSettings` | bool | Whether the plugin implements `IPluginSettings` (config schema available) |
| `hasCameraSettings` | bool | Whether the plugin implements `IPluginCameraSettings` |
| `hasStreamSettings` | bool | Whether the plugin implements `IPluginStreamSettings` |

#### GET /api/v1/plugins/{id}

Get plugin details (same fields as the list item).

#### OPTIONS /api/v1/plugins/{id}/config

Get the settings schema for a plugin. Returns the grouped field definitions from `IPluginSettings.GetSchema()`. Returns an empty array if the plugin does not implement `IPluginSettings`. The UI should call this first and only show config options if the schema is non-empty.

**Response body:**

Array of setting groups:

| Field | Type | Description |
|-------|------|-------------|
| `key` | string | Group identifier |
| `order` | int | Display order |
| `label` | string | Group heading |
| `description` | string? | Group description |
| `fields` | object[] | Setting fields |

Field object:

| Field | Type | Description |
|-------|------|-------------|
| `key` | string | Field identifier |
| `order` | int | Display order within group |
| `label` | string | Field label |
| `type` | string | Field type (e.g. `string`, `int`, `bool`, `path`, `password`) |
| `description` | string? | Help text |
| `defaultValue` | any? | Default value |
| `required` | bool | Whether the field is required |
| `value` | any? | Current value (from `IPluginSettings.GetValues()`) |

#### GET /api/v1/plugins/{id}/config

Get current config values for a plugin. Returns the values from `IPluginSettings.GetValues()`. Returns `BadRequest` if the plugin does not implement `IPluginSettings`.

**Response body:** Key-value object of current settings.

#### PUT /api/v1/plugins/{id}/config

Update plugin configuration. Calls `IPluginSettings.ApplyValues()`. Returns validation errors on failure. Returns `BadRequest` if the plugin does not implement `IPluginSettings`.

#### POST /api/v1/plugins/{id}/config/validate

Validate a single field value. Calls `IPluginSettings.ValidateValue()`. Returns `BadRequest` with a message if validation fails. Returns `BadRequest` if the plugin does not implement `IPluginSettings`. Used by the UI for inline validation on field blur.

**Request body:**

| Field | Type | Description |
|-------|------|-------------|
| `key` | string | Field key to validate |
| `value` | any | Value to validate |

#### POST /api/v1/plugins/{id}/start

Start a stopped plugin. Only available for plugins that implement `IUserStartable`. Returns `Unavailable` if the plugin does not support user-initiated start.

#### POST /api/v1/plugins/{id}/stop

Stop a running plugin. Only available for plugins that implement `IUserStartable`. Returns `Unavailable` if the plugin does not support user-initiated stop.

---

## Streaming

Live and recording playback use a unified WebSocket endpoint with a binary protocol. The client sends commands (go-live, fetch) and the server pushes mux fragments. See [protocol.md](protocol.md) for the tunnel protocol equivalent.

#### GET /api/v1/stream/{cameraId}

WebSocket upgrade. Opens a bidirectional streaming session for the specified camera. The session supports both live and playback through a command-based protocol:

**Client messages** (binary):

| Type byte | Name | Fields |
|-----------|------|--------|
| `0x01` | Live | profile (length-prefixed UTF-8) |
| `0x02` | Fetch | profile (length-prefixed UTF-8), from (uint64 BE), to (uint64 BE) |

**Server messages** (binary):

| Type byte | Name | Fields |
|-----------|------|--------|
| `0x01` | Init | profile (length-prefixed UTF-8), data (init segment, format-defined) |
| `0x02` | Gop | flags (byte), profile (length-prefixed UTF-8), timestamp (uint64 BE), data (mux fragment) |
| `0x03` | Status | status byte: Ack (0x00), FetchComplete (0x01), Gap (0x02 + from/to uint64 BE), Error (0x04), Live (0x05), Recording (0x06) |

The client sends a `Live` command to start receiving the live stream, or a `Fetch` command to request recorded data for a time range. The server responds with `Init` (when the format defines one), followed by `Gop` messages (mux fragments), and `Status` messages to signal mode, gaps, and fetch completion. The client can switch between live and playback at any time by sending a new command.
