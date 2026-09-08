# SimpleVMS

[![CI](https://github.com/SytheZN/simple-vms/actions/workflows/ci.yml/badge.svg)](https://github.com/SytheZN/simple-vms/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/SytheZN/simple-vms?label=release)](https://github.com/SytheZN/simple-vms/releases/latest)
[![Pre-release](https://img.shields.io/github/v/release/SytheZN/simple-vms?include_prereleases&label=prerelease)](https://github.com/SytheZN/simple-vms/releases)
[![Container](https://ghcr-badge.egpl.dev/sythezn/simple-vms/latest_tag?label=container)](https://github.com/SytheZN/simple-vms/pkgs/container/simple-vms)
[![License](https://img.shields.io/github/license/SytheZN/simple-vms)](LICENSE)

A network video management system for home and power users. Supports up to 32 cameras on a single node with no transcoding, minimal resource usage, and first-class ONVIF support.

> **Note:** This project is under active development and APIs may change without notice.

![Web UI screenshot](docs/images/screenshot.png)

## Highlights

- **Zero transcoding** - video passes through as raw data units
- **Compressed-domain motion detection** - motion vectors and skip flags are read from the H.264/H.265 entropy layer; no frames are ever reconstructed
- **Single port access** - all native client communication (API, live video, playback, events) multiplexed over one TCP/TLS port
- **Plugin-first architecture** - capture, storage, formats, analytics, auth, and notifications are all behind extension point interfaces with no privileged internal code paths
- **Cross-platform clients** - native desktop (Windows, Linux, macOS), mobile (Android), and web UI
- **Mutual TLS** - clients authenticate with certificates signed by a server-generated root CA; revocation is immediate
- **Network-storage-safe** - sequential writes, no mmap, no flock; runs directly against NFS or SMB mounts
- **CPU-only by default** - runs efficiently on hardware without a GPU

## Documentation

See the [`docs/`](docs/) directory for architecture, deployment, API reference, protocol specification, and more.

## Getting Started

### Install

Every [release](https://github.com/SytheZN/simple-vms/releases) ships:

- Server tarball for Linux x64, or the [container image](https://github.com/SytheZN/simple-vms/pkgs/container/simple-vms) via the included [`docker-compose.yml`](docker-compose.yml)
- Desktop installers: Windows installer, macOS DMG, Linux AppImage
- Android APK

### Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/) and [Node.js 22+](https://nodejs.org/) for the web UI.

```bash
./build.sh build     # build
./build.sh test      # run tests
```

Packaging scripts for each platform are under [`scripts/publish/`](scripts/publish/).

### Run

```bash
./out/server/Server
./out/server/Server --data-path /mnt/nas/data
```

On first run, the web UI at `http://localhost:8080` serves a setup wizard to configure the data provider and generate certificates. After setup, discover cameras or add them manually.

See [Deployment](docs/deployment.md) for Docker Compose, systemd, and other installation methods.

## Technology Stack

| Component      | Technology                                      |
| -------------- | ----------------------------------------------- |
| Server         | .NET 10, ASP.NET Core (Kestrel)                 |
| Transport      | TCP + TLS 1.3 (SslStream), mutual TLS           |
| Serialization  | MessagePack (tunnel framing), JSON (API bodies) |
| Database       | Pluggable via `IDataProvider` (SQLite included) |
| Web UI         | Vue.js 3 + Vite                                 |
| Native clients | Avalonia UI + FFmpeg                            |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on submitting changes, issues, etc.

## License

This project is licensed under the [MIT License](LICENSE).
