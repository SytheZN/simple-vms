# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Per-camera privacy pause with a set duration
- Input validation caters for interdependent values

### Changed

- Retention balances storage across cameras and reduces proportionally when space is short

### Fixed

- Playback stutter and timeline glitches in the web client
- Camera events and motion data not pruned with recordings in some circumstances

## [0.1.2] - 2026-10-02

### Changed

- New sVMS logo

### Fixed

- Missing browser tab icon in the web client

## [0.1.1] - 2026-09-17

### Fixed

- Server memory and connection leaks during long-running recording
- Cameras staying offline after a failed reconnect

## [0.1.0] - 2026-09-08

First release of SimpleVMS (sVMS)!

### Added

- ONVIF camera discovery, setup, and event handling
- RTSP camera capture for H.264 and H.265
- Continuous recording with per-camera retention policies
- Live and playback video with a scrubbable timeline
- Camera events surfaced alongside the timeline
- System events for configuration changes, enrollment, and recording state, shown alongside camera events
- Per-stream storage breakdown showing size, duration, and rate
- Web client with camera gallery, live and playback viewing, events, timeline, and settings
- Gallery thumbnails decoded from camera keyframes
- Native desktop apps for Windows, macOS, and Linux with system tray, gallery, camera view, and settings
- Native Android app with background tunnel, encrypted credential storage, and QR enrollment
- Hardware-accelerated video playback on every native platform
- Motion detection shown as a live overlay on every client
- Light and dark themes shared across web and native clients
- Encrypted credential storage using each platform's secure store
- Encrypted client-server connections with certificate pinning
- Remote access with automatic port forwarding (UPnP and NAT-PMP) and public address verification
- Guided first-run setup
- Client enrollment by QR code or short token
- Plugin system for adding cameras, storage backends, muxers, analyzers, and authentication providers
- Per-camera and per-stream settings contributed by plugins
- Platform-native installers: macOS DMG, Linux AppImage, Windows installer

<!-- link references -->
[Unreleased]: https://github.com/SytheZN/simple-vms/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/SytheZN/simple-vms/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/SytheZN/simple-vms/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/SytheZN/simple-vms/compare/v0.0.0...v0.1.0
