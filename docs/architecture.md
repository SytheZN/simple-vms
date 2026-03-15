# Architecture

## Overview

A network video management system designed for home and power users. It supports up to 32 cameras on a single node with no transcoding, minimal resource usage, and first-class ONVIF support.

## Design Principles

- **No transcoding** - video passes through as opaque NAL units; the server never decodes or re-encodes
- **Single port access** - all client communication (API, live video, playback, events) over one QUIC/UDP port
- **Plugin-first** - all major subsystems (capture, storage, formats, detection) are behind extension point interfaces; built-in functionality uses the same interfaces as third-party plugins
- **CPU-only by default** - the server must run efficiently on hardware without a GPU; hardware acceleration is a future plugin concern
- **Containers first, native supported** - Docker/Podman is the primary deployment, but the server runs as a standalone binary with no container dependency

## Technology Stack

| Component | Technology | Notes |
|-----------|-----------|-------|
| Server runtime | .NET 10 | LTS, cross-platform, AOT-capable |
| Server framework | ASP.NET Core (Kestrel) | HTTP for web UI; QUIC for native clients |
| Database | Pluggable via `IDataProvider` | Metadata, indexes, config |
| Web UI | Vue.js 3 + Vite | Embedded SPA served by Kestrel |
| Client framework | Avalonia UI | Shared core + per-platform shells |
| Client video | LibVLCSharp | Hardware-accelerated decode on client devices |
| Secure transport | QUIC (System.Net.Quic / msquic) | Mutual TLS, multiplexed, single UDP port |

## System Topology

```mermaid
graph TB
    subgraph home["Home Network"]
        cam1["Camera 1"] -- RTSP --> server
        cam2["Camera 2"] -- RTSP --> server
        camN["Camera N"] -- RTSP --> server

        subgraph server["VMS Server"]
            db["Database"]
        end

        server -- NFS --> nas["NAS (data + recordings)"]
        server -- "QUIC :443" --> lanClients["Clients (LAN)"]
    end

    server -- "Port forward UDP :443" --> remoteClients["Clients (remote)"]
```

## Server Architecture

### Module Overview

```
Shared.Models             > Domain models, DTOs, extension point interfaces, events
Shared.Protocol           > QUIC protocol definitions, framing, stream types

Server                    > ASP.NET Core host, startup, DI composition
Server.Api                > HTTP endpoints (web UI, enrollment), middleware
Server.Onvif              > ONVIF client (discovery, device, media, events, analytics)
Server.Streaming          > RTSP ingest, NAL demux, live stream fan-out
Server.Recording          > fMP4 segment writer, keyframe indexer, retention engine
Server.Storage            > Storage provider abstraction (NFS built-in)
Server.Data               > Data provider abstraction, repository interfaces
Server.Tunnel             > QUIC listener, mutual TLS, stream dispatch
Server.Plugins            > Plugin host, discovery, lifecycle management

Client.Core               > ViewModels, services, shared controls (Avalonia)
Client.Desktop            > Windows/Linux/macOS shell, tray, desktop VPN
Client.Android            > Android shell, notifications, QR scanning
Client.iOS                > iOS shell, notifications, QR scanning

Client.Web                > Vue.js SPA (built assets embedded in Server)
```

### Data Flow: Camera to Client

```mermaid
sequenceDiagram
    participant Camera
    participant Server
    participant NFS
    participant Database
    participant Client

    Camera->>Server: RTSP/TCP stream

    Note over Server: Stream Ingestor<br/>1. RTP demux<br/>2. NAL extract<br/>3. Fan-out

    par Recording
        Server->>NFS: fMP4 segment
        Server->>Database: Keyframe index
    and Live view
        Server->>Client: QUIC stream (fMP4 fragments)
    end

    Note over Client: Playback request

    Client->>Server: QUIC stream (timestamp)
    Server->>Database: Lookup nearest keyframe
    Database-->>Server: Segment + byte offset
    Server->>NFS: Seek to offset
    NFS-->>Server: fMP4 data
    Server->>Client: QUIC stream (fMP4 from keyframe)
```

### Internal Event Bus

The server uses `System.Threading.Channels` for internal pub/sub:

- `CameraStatusChanged` - camera online/offline/error
- `StreamStarted` / `StreamStopped`
- `RecordingSegmentCompleted` - new segment written
- `MotionDetected` / `MotionEnded`
- `OnvifEvent` - raw ONVIF events (tamper, analytics, I/O)
- `ClientConnected` / `ClientDisconnected`

Plugins subscribe to these channels to react to system events. The event bus is in-process only (not distributed - single node design).

### Stream Profiles

A camera exposes multiple stream profiles, which fall into two categories:

**Quality profiles** (`main`, `sub`, etc.) - video streams at different resolutions/bitrates. The client subscribes to one at a time, typically choosing based on network conditions (e.g. `sub` on cellular, `main` on LAN). Each quality profile has its own retention policy (e.g. keep `main` for 30 days, `sub` for 90 days).

**Metadata profiles** (`motion`, etc.) - non-video data streams produced from camera analytics. These use the same subscription, recording, and playback pipeline as quality profiles but with a different `IStreamFormat` implementation suited to their data. The client subscribes to a metadata profile alongside a quality profile and renders it as an overlay.

Metadata profiles do not have their own retention. A metadata segment is retained as long as any quality profile has a corresponding segment covering that time range - when the last overlapping quality segment is purged, the metadata segment is purged with it.

The ONVIF provider registers a `motion` profile on cameras that support analytics metadata. The motion data (active cell grids, regions) is recorded as timestamped segments and can be played back in sync with video.

### Recording: Segment Format

Recordings are stored as **fragmented MP4 (fMP4)** files:

- No transcoding - NAL units from RTSP are remuxed into ISO BMFF containers
- 5-minute segments by default (configurable per camera, actual duration rounds to the nearest keyframe boundary)
- Each segment is independently playable
- Keyframe byte offsets indexed in the database for sub-second seek

Segment write pipeline:

```mermaid
graph LR
    nal["NAL units"] --> muxer["fMP4 Muxer\n(pure C#)"]
    muxer --> nfs["File write (NFS)"]
    muxer --> db["Index update (Database)"]
```

The muxer handles:
- H.264: Annex B > AVC (length-prefixed NALUs), SPS/PPS in `avcC` box
- H.265: Annex B > HEVC (length-prefixed NALUs), VPS/SPS/PPS in `hvcC` box
- Timing derived from RTP timestamps
- ISO BMFF box layout: `ftyp`, `moov` (with `mvex`), then repeating `moof`+`mdat`

### Storage Layout

The server host is stateless - all persistent data lives on configured storage paths (local or network). The server's I/O patterns are network-storage-safe (sequential writes, no mmap, no filesystem locking, latency tolerant).

The server takes a single `--data-path` argument for core data (certs, plugins). The recordings path is configured through the `IStorageProvider` plugin via the database.

```
{data-path}/
  certs/                               # root CA, server cert, client certs
  plugins/                             # plugin assemblies
```

The recordings path layout is managed by the `IStorageProvider` plugin. The layout below is an example from the built-in filesystem provider - other providers may organize data differently:

```
{recordings-path}/
  {camera_uuid}/
    {stream_profile}/                  # "main", "sub", etc.
      {year}/{month}/{day}/
        {timestamp}.mp4                # e.g. 20260315T140000Z.mp4
```

- Flat date directories for simple manual inspection
- Lexicographic filename sort equals chronological sort
- Metadata storage is managed by the `IDataProvider` plugin
- Server can be rebuilt/replaced without data loss - just point at the same storage and start

### Retention Engine

Runs periodically (configurable interval, default 15 minutes). Per-camera retention policies:

| Mode | Behavior |
|------|----------|
| `days` | Delete segments older than N days |
| `bytes` | Delete oldest segments when total exceeds N bytes |
| `percent` | Delete oldest segments when mount usage exceeds N% |

Policies are evaluated in order: days first, then size/percent. Deletion is oldest-first within each camera's segments.

Retention is configured per quality profile - different profiles can have different policies (e.g. keep `main` for 30 days, `sub` for 90 days). Metadata profiles (`motion`, etc.) do not have their own retention. A metadata segment is retained as long as any quality profile has a segment covering the same time range. When the last overlapping quality segment is purged, the metadata segment is purged with it.

## Plugin System

See [plugins.md](plugins.md) for full specification.

### Extension Points

| Interface | Purpose | Built-in Implementations |
|-----------|---------|------------------------|
| `ICaptureSource` | Acquire video from a source | RTSP (TCP interleaved) |
| `IStreamFormat` | Mux/demux stream container format (video, audio, metadata) | Fragmented MP4 |
| `ICameraProvider` | Camera-specific behavior, discovery | ONVIF, Generic RTSP |
| `IEventFilter` | Process and filter events | Motion zone filter |
| `INotificationSink` | Deliver notifications | Client push (QUIC) |
| `IVideoAnalyzer` | Analyze video frames (requires decode) | None (future) |
| `IStorageProvider` | Read/write recordings | NFS/local filesystem |
| `IDataProvider` | Metadata storage (cameras, segments, events, config, etc.) | TBD |
| `IAuthProvider` | Authenticate HTTP/web UI requests | None (unauthenticated by default) |
| `IAuthzProvider` | Authorize operations and filter results by identity | Built-in RBAC (no provider = unrestricted `su` access) |

### Plugin Services

Plugins receive these services via dependency injection:

| Service | Purpose |
|---------|---------|
| `IEventBus` | Publish and subscribe to system events |
| `IStreamTap` | Subscribe to raw NAL unit streams from cameras |
| `IRecordingAccess` | Query and read recording segments |
| `ICameraRegistry` | Enumerate cameras and stream profiles |
| `IPluginConfig` | Read plugin-specific configuration |
| `IPluginDataStore` | Per-plugin isolated key-value/document store for internal state |

### Plugin Loading

- Plugins are .NET assemblies placed in the `plugins/` directory
- Each assembly contains a class implementing `IPlugin`
- The plugin host discovers, loads, and manages plugin lifecycle
- Plugins register their extension point implementations via `IServiceCollection`
- Built-in modules use the same interfaces (no privileged internal paths)

## Security Model

- QUIC transport with mutual TLS (client certificates)
- Server generates a self-signed root CA on first run
- Each client receives a unique certificate signed by the root CA
- Certificate revocation is immediate (delete client record, reject on next handshake)
- ONVIF camera credentials stored encrypted in the database
- Web UI accessible on local network only (HTTP, unauthenticated by default - pluggable via `IAuthProvider`)
- Enrollment via QR code (mobile) or short token over LAN (desktop)

## Resource Budget (32 cameras)

Target resource usage for 32 cameras, dual stream each (main 1080p + sub 360p):

| Resource | Budget | Notes |
|----------|--------|-------|
| CPU | < 2 cores sustained | No decode; RTSP demux + fMP4 mux only |
| Memory | < 512 MB | Per-stream buffers (~2 MB each), DB pool, framework |
| Disk I/O | Pass-through | Bound by camera bitrate sum, written to NFS |
| Network (ingest) | ~256 Mbps | 32 × 8 Mbps main + 32 × 512 Kbps sub |
| Network (client) | Per viewer | One stream per live view |
| QUIC port | 1 UDP | All client communication |
| HTTP port | 1 TCP | Web UI only (LAN) |
