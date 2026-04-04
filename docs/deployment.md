# Deployment

## Overview

The server supports two deployment modes:

- **Container** (Docker/Podman) - primary, recommended for most users
- **Native** - standalone binary, for users who prefer direct installation

Both modes require:
- A data path for persistent data (local or network - the server's I/O patterns are network-storage-safe: no memory-mapped files, no filesystem locking, tolerant of latency)
- A configured `IDataProvider` plugin (database)
- A configured `IStorageProvider` plugin (recording storage)
- TCP port for tunnel (client access)
- TCP port for HTTP (web UI, enrollment - LAN only)

The server host is stateless apart from the binary itself. All persistent state lives under the data path.

## Startup Arguments

| Argument | Default | Description |
|----------|---------|-------------|
| `--data-path` | `./data` | Root path for persistent data (certs, plugins) |
| `--tunnel-port` | `4433` | TCP port for tunnel client connections |
| `--http-port` | `8080` | TCP port for HTTP web UI and enrollment |
| `--bind` | `0.0.0.0` | Bind address |

All other configuration (cameras, retention, settings, plugin config) is stored in the database via `IDataProvider`.

## Data Path Layout

```
{data-path}/
  certs/       # root CA, server cert, client certs
  plugins/     # plugin assemblies
```

The recordings path is managed by the `IStorageProvider` plugin, configured through the database. It may be under the data path or elsewhere entirely.

## Container Deployment

### Docker Compose

```yaml
services:
  server:
    image: <image>:latest
    restart: unless-stopped
    ports:
      - "4433:4433"      # Tunnel (clients)
      - "8080:8080"      # HTTP (web UI, LAN only)
    volumes:
      - /path/to/data:/data
    command: ["--data-path", "/data"]
    # No special capabilities required
```

No sidecar database container is required by default - the data provider plugin manages its own storage. If the chosen data provider is a client-server database, the user provisions that separately.

### Podman

Same compose file works with `podman-compose` or `podman play kube`. No root required unless the tunnel port is below 1024 (can be changed to a high port and mapped externally).

## Native Deployment

### Installation

The server ships as a single self-contained .NET binary. No runtime installation required (self-contained publish).

```bash
# Download and extract
tar xzf <archive>-linux-x64.tar.gz -C /opt/server

# Start with defaults (data in ./data)
/opt/server/server

# Or specify a data path
/opt/server/server --data-path /mnt/nas/data
```

### systemd

```ini
[Unit]
Description=<name> Server
After=network-online.target remote-fs.target
Wants=network-online.target remote-fs.target

[Service]
Type=notify
ExecStart=/opt/server/server --data-path /mnt/nas/data
Restart=on-failure
RestartSec=5
User=vms
Group=vms
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/mnt/nas/data

[Install]
WantedBy=multi-user.target
```

### launchd (macOS)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.<name>.server</string>
    <key>ProgramArguments</key>
    <array>
        <string>/opt/server/server</string>
        <string>--data-path</string>
        <string>/Volumes/NAS/vms</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
</dict>
</plist>
```

### Windows Service

The binary supports running as a Windows service via `Microsoft.Extensions.Hosting.WindowsServices`:

```powershell
sc.exe create <ServiceName> binPath="C:\server\server.exe --data-path D:\server-data"
sc.exe start <ServiceName>
```

## Storage

The data path and any recording paths can point to local directories, network mounts (NFS, SMB/CIFS), or any filesystem the OS can present as a path.

The server's I/O patterns are designed to be network-storage-safe:
- Sequential writes for recording segments
- No memory-mapped files
- No filesystem-level locking
- Tolerant of latency on reads

### Notes

- If using network mounts, ensure they are available before the server starts (`After=remote-fs.target` in systemd, or equivalent)
- The server will refuse to start if the data path is not accessible
- Write performance matters for the recordings path - the server writes at the aggregate bitrate of all cameras (see resource budget in [architecture.md](architecture.md))

## Networking

### Ports

| Port | Protocol | Purpose | Exposure |
|------|----------|---------|----------|
| 4433 (configurable) | TCP | Tunnel - all native client communication | LAN + port forward for remote access |
| 8080 (configurable) | TCP | HTTP - web UI, enrollment API | LAN only |

### Remote Access

For remote client access, forward the tunnel TCP port through the router to the server. The server's external endpoint (hostname/IP) is configured in settings and included in enrollment payloads.

Dynamic DNS services (DuckDNS, No-IP, etc.) work well for home connections with changing IPs. The server does not manage DDNS - use the provider's update client separately.

### Firewall

The server needs:
- Outbound RTSP/TCP to cameras (typically port 554)
- Outbound ONVIF/HTTP to cameras (typically port 80/443)
- Inbound TCP on the tunnel port
- Inbound TCP on the HTTP port (LAN only)

## First Run

The server distinguishes between a fresh installation and an existing one by checking for expected artifacts in the data path (e.g. the root CA certificate in `{data-path}/certs/`).

### New Installation

If no existing artifacts are found, the server starts in **setup mode**:

1. Server starts, reads startup arguments
2. HTTP web UI serves the setup wizard only - no other functionality is available
3. The wizard warns the user: if they expect an existing installation, the data path may be misconfigured (e.g. a network mount that isn't mounted)
4. The user selects and configures a data provider
5. The user confirms this is a new installation by clicking "Create Certs"
6. Server generates root CA and server certificate, stores in `{data-path}/certs/`
7. Server starts the data provider plugin (which runs schema migration), then starts remaining plugins
8. Server transitions to normal operation
9. User runs camera discovery or adds cameras manually
10. User creates a client enrollment

This prevents a missing network mount from silently bootstrapping a fresh installation on top of an empty local directory.

### Existing Installation

If existing artifacts are found, the server starts normally. Plugins and storage may not be immediately available (e.g. delayed network mount). The server handles this gracefully:

- If certificates are found, startup proceeds normally
- If certificates are not found, the server polls for their existence in the background while the web UI redirects to the new installation flow. If the certificates later appear (whether from the user clicking "Create Certs" or from a delayed mount), the server continues startup and the UI redirects to the main page.
- Startup does not fail if plugins or storage are temporarily unavailable
- The server retries loading plugins and connecting to storage in the background with backoff
- Cameras and recording start as their dependencies become available
- The web UI and tunnel listener come up immediately - clients can connect and will see cameras appear as they come online
- System health reflects the degraded state until all dependencies are resolved

## Upgrades

- **Container**: Pull new image, restart. The data provider handles any schema migrations on startup.
- **Native**: Replace the binary, restart the service. Same migration behavior.

Plugins are upgraded independently by replacing their assemblies in the `plugins/` directory and restarting.

## Backup

All persistent state lives under the data path (and any recording paths configured in the storage provider). Backing up these paths captures everything. The binary itself is the only thing outside the data path.
