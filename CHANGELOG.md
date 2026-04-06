# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

First release of the Simple-VMS Server!

### Added

- Plugin system with isolated loading, lifecycle management, and per-plugin settings
- ONVIF plugin for camera discovery, device management, events, and analytics
- RTSP capture plugin with TCP interleaved transport and session sharing
- Fragmented MP4 muxer plugin (H.264/H.265)
- SQLite data provider plugin with schema migration
- Filesystem storage plugin
- Streaming pipeline with typed data/video streams and automatic plugin matching
- Recording with configurable segment duration and keyframe indexing
- Retention engine with per-stream policies
- Event-driven camera lifecycle with probe/refresh and per-profile status tracking
- Transport-agnostic API dispatcher with pattern matching and route constraints
- Authentication and authorization middleware with plugin-based providers
- TCP+TLS tunnel transport with stream multiplexing and keepalive
- Client enrollment via QR code or short token
- Web client with camera gallery, live/playback streaming, events, timeline, and settings
- WebCodecs video player with MSE fallback
- Native client core library with camera grid, video player, timeline, and notifications
- Source-generated JSON serialization
- Build script with code coverage collection and report merging

<!-- link references -->
[Unreleased]: https://github.com/SytheZN/simple-vms/compare/v0.0.0...HEAD
