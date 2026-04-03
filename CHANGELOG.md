# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- ONVIF camera discovery, device management, events, and analytics
- RTSP capture source (TCP interleaved)
- Fragmented MP4 muxer (H.264/H.265)
- Stream pipeline with typed data/video streams and automatic plugin matching
- Recording with configurable segment duration and keyframe indexing
- Retention engine (days, bytes, percent modes) with per-stream policies
- SQLite data provider with schema migration
- Filesystem storage provider (NFS, local)
- Plugin system with extension points, isolated loading, and lifecycle management
- QUIC transport with mutual TLS and multiplexed streams
- Client enrollment via QR code or short token
- Web UI with camera gallery, events, client management, and settings
- WebSocket streaming (live and playback) for web clients
- Event-driven camera lifecycle management
- Camera probe/refresh API
- RTSP session sharing with multi-track support
