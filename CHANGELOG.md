# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

First release of SimpleVMS (sVMS)!

### Added

- ONVIF camera discovery, setup, and event handling
- RTSP camera capture for H.264 and H.265
- Continuous recording with per-camera retention policies
- Live and playback video with a scrubbable timeline
- Camera events surfaced alongside the timeline
- Per-stream storage breakdown showing size, duration, and rate
- Web client with camera gallery, live and playback viewing, events, timeline, and settings
- Native desktop apps for Windows, macOS, and Linux with system tray, gallery, camera view, and settings
- Native Android app with background tunnel, encrypted credential storage, and QR enrollment
- Hardware-accelerated video playback on every native platform
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
[Unreleased]: https://github.com/SytheZN/simple-vms/compare/v0.0.0...HEAD
