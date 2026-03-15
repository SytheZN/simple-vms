# VMS

A network video management system for home and power users. Supports up to 32 cameras on a single node with no transcoding, minimal resource usage, and first-class ONVIF support.

## Prerequisites

- .NET 10 SDK
- Node.js 22+ (for web UI)

## Build

```bash
./build.sh build
```

## Test

```bash
./build.sh test
```

## Publish

```bash
./build.sh publish
```

Build artifacts are written to `/out`.

## Project Structure

```
src/
  Shared.Models/       Domain models, DTOs, interfaces
  Shared.Protocol/     QUIC protocol, framing, serialization
  Server/              ASP.NET Core host, startup, DI
  Server.Api/          HTTP endpoints, middleware
  Server.Onvif/        ONVIF client (discovery, device, media, events)
  Server.Streaming/    RTSP ingest, NAL demux, live fan-out
  Server.Recording/    fMP4 muxer, segment writer, retention
  Server.Storage/      Storage provider abstraction
  Server.Data/         Data provider, repositories
  Server.Tunnel/       QUIC listener, mutual TLS, stream dispatch
  Server.Plugins/      Plugin host, discovery, lifecycle
  Client.Core/         Shared ViewModels, services, controls (Avalonia)
  Client.Desktop/      Windows/Linux/macOS shell
  Client.Android/      Android shell
  Client.iOS/          iOS shell
  Client.Web/          Vue.js SPA
tests/
  Tests.Unit/          Unit tests
  Tests.Integration/   Integration tests
```
