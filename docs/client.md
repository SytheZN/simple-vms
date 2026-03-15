# Client Architecture

## Overview

The client is built with Avalonia UI, structured as a shared core library containing all ViewModels, services, and reusable controls, with per-platform shell projects that provide navigation chrome, layout, and platform-specific service implementations.

After enrollment, all communication with the server goes through the QUIC tunnel. The only exception is the enrollment process itself, which uses HTTP on the local network to exchange a token for credentials (see [enrollment.md](enrollment.md)).

## Project Structure

```
Client.Core/                    # Shared library (Avalonia)
  Models/                       # Domain models (Camera, Stream, Event, etc.)
  ViewModels/                   # MVVM ViewModels
  Services/                     # API client, tunnel, notifications (abstractions)
  Controls/                     # Shared custom controls
  Converters/                   # Value converters

Client.Desktop/                 # Windows, Linux, macOS
  Views/                        # Desktop-specific layouts
  Services/                     # Platform service implementations
  Program.cs

Client.Android/                 # Android
  Views/                        # Mobile layouts
  Services/                     # Android-specific implementations
  AndroidManifest.xml

Client.iOS/                     # iOS
  Views/                        # iOS layouts
  Services/                     # iOS-specific implementations
  Info.plist
```

## Shared Core

### ViewModels

All ViewModels live in `Client.Core` and are shared across all platforms. They contain all presentation logic and interact with the server exclusively through services.

| ViewModel | Responsibility |
|-----------|---------------|
| `GalleryViewModel` | Camera grid - layout, ordering, grouping, thumbnail streams |
| `CameraViewModel` | Single camera view - live/playback toggle, stream quality selection |
| `TimelineViewModel` | Timeline state - current position, zoom level, recording spans, events |
| `EventsViewModel` | Event list - filtering, pagination, navigation to playback |
| `SettingsViewModel` | Server connection, enrollment, notification preferences |
| `EnrollmentViewModel` | QR scan / token entry flow |

### Services

Service interfaces are defined in the core. Platform shells provide implementations where needed.

| Service | Core or Platform | Description |
|---------|-----------------|-------------|
| `ITunnelService` | Core | QUIC connection management, address selection, reconnection |
| `IApiClient` | Core | Request/response over QUIC API streams |
| `ILiveStreamService` | Core | Subscribe/unsubscribe to live camera streams |
| `IPlaybackService` | Core | Playback stream management, seek |
| `IEventService` | Core | Event channel subscription, event queries |
| `ICredentialStore` | Platform | Secure storage for enrollment credentials |
| `INotificationService` | Platform | Local/push notifications |
| `IQrScannerService` | Platform | Camera-based QR code scanning |
| `ITrayService` | Platform (desktop) | System tray icon and menu |

### Controls

Reusable Avalonia controls shared across all platform layouts.

| Control | Description |
|---------|-------------|
| `CameraGrid` | Configurable grid of camera thumbnails with drag-to-reorder |
| `VideoPlayer` | Video surface backed by LibVLCSharp, handles live and playback |
| `Timeline` | Horizontal timeline bar with scrubbing, zoom, recording spans, event markers |
| `QrScanner` | Viewfinder with QR detection (delegates to platform `IQrScannerService`) |
| `StreamQualitySelector` | Dropdown/toggle for available stream profiles |

## Platform Shells

Each shell provides:

1. **Navigation chrome** - how the user moves between screens
2. **Layout containers** - how shared controls are arranged
3. **Platform services** - implementations of platform-specific interfaces

### Desktop (Windows, Linux, macOS)

Single window with a side navigation rail and a content area.

```
┌──────────┬─────────────────────────────────────────┐
│          │                                         │
│ Gallery  │           Content Area                  │
│          │                                         │
│ Camera   │  Gallery:   3-4+ column camera grid     │
│          │  Camera:    Large player + side timeline│
│ Events   │  Events:   Filterable table             │
│          │  Settings: Form layout                  │
│ Settings │                                         │
│          │                                         │
└──────────┴─────────────────────────────────────────┘
```

- Side rail icons for Gallery, Camera, Events, Settings
- Camera view: player fills most of the width, timeline panel on the right
- Gallery grid columns are user-configurable
- System tray icon with connection status, quick access to gallery
- Keyboard shortcuts for common actions (fullscreen, next/prev camera, play/pause)

### Android

Bottom navigation bar with full-width content.

```
┌─────────────────────────────────────┐
│                                     │
│          Content Area               │
│                                     │
│  Gallery:  1-2 column grid          │
│  Camera:   Full-width player,       │
│            bottom sheet timeline    │
│  Events:   Scrollable list          │
│  Settings: Form layout              │
│                                     │
├─────────────────────────────────────┤
│ Gallery  Camera  Events  Settings   │
└─────────────────────────────────────┘
```

- Bottom nav: Gallery, Camera, Events, Settings
- Camera view: player is full-width, timeline slides up as a bottom sheet
- Pinch to zoom on live/playback, double-tap for fullscreen
- Gallery columns adapt to screen width (1 on phone, 2 on small tablet)
- Swipe between cameras in single view

### iOS

Tab bar navigation following iOS conventions.

```
┌─────────────────────────────────────┐
│          Content Area               │
│                                     │
│  Same layout as Android but         │
│  following iOS design patterns:     │
│  - Large titles                     │
│  - Native scroll behavior           │
│  - Haptic feedback on interactions  │
│                                     │
├─────────────────────────────────────┤
│ Gallery  Camera  Events  Settings   │
└─────────────────────────────────────┘
```

- Tab bar: Gallery, Camera, Events, Settings
- Same functional layout as Android, adapted to iOS conventions
- Uses iOS-native gestures and transitions where Avalonia supports them

## Video Playback

Video playback uses LibVLCSharp with the Avalonia integration.

### Live View

1. `ILiveStreamService` opens a QUIC live subscribe stream (`0x0300`)
2. Server sends init segment (`ftyp` + `moov`) followed by continuous fMP4 fragments
3. Fragments are fed to LibVLC via a custom `MediaInput` stream
4. LibVLC handles hardware-accelerated decode on the client device

### Playback

1. `IPlaybackService` opens a QUIC playback stream (`0x0301`) with a target timestamp
2. Server seeks to nearest keyframe, streams fMP4 from there
3. Same LibVLC pipeline as live view
4. Seeking: close current stream, open new one with new timestamp (cheap - no QUIC round-trip overhead for stream creation)

### Quality Selection

The client subscribes to one quality profile at a time (`main`, `sub`, etc.). The user can switch between them in the camera view. Switching closes the current stream and opens a new one with the selected profile.

On mobile, the client defaults to the lower quality profile over cellular and the highest available on WiFi. This is a user-configurable preference.

### Motion Overlay

If the camera provides a `motion` metadata profile, the client subscribes to it alongside the selected quality profile. The motion stream delivers timestamped cell grid data which the `VideoPlayer` control renders as a translucent overlay on the video, synced by timestamp.

The same mechanism works for both live and playback - motion data is recorded and played back like any other stream profile. The overlay can be toggled on/off by the user.

## Timeline

The timeline is a core visual component showing:

- **Recording spans** - continuous blocks where recording exists (filled bars)
- **Events** - markers/highlights at event times (color-coded by type)
- **Current position** - playhead indicator

### Interaction

- **Drag** the playhead to scrub through time
- **Pinch/scroll** to zoom the time scale (from minutes to days)
- **Tap** an event marker to jump to that timestamp
- Smooth transition between live and playback - dragging the playhead off the live edge starts playback, dragging back to the edge resumes live

### Data Loading

The timeline fetches data from `GET /api/v1/recordings/{cameraId}/timeline` which returns condensed spans and events for the visible time range. As the user zooms or pans, new data is fetched. The client caches timeline data to avoid redundant requests.

## Enrollment

### QR Flow (Mobile)

1. User taps "Add Server" in settings
2. Client opens the QR scanner (platform `IQrScannerService`)
3. On scan, parses the JSON payload (address + token)
4. Calls `POST /api/v1/enroll` with the token on the first reachable address
5. Stores received credentials via `ICredentialStore`
6. Connects via QUIC

### Token Flow (Desktop)

1. User taps "Add Server" in settings
2. Enters server address and token manually
3. Same enrollment API call and credential storage

See [enrollment.md](enrollment.md) for the full flow.

## Background Service

The client maintains a background connection to the server for event notifications.

| Platform | Mechanism |
|----------|-----------|
| Windows | Background process / system tray |
| Linux | Background process / system tray |
| macOS | Background process / menu bar item |
| Android | Foreground service (persistent notification required by OS) |
| iOS | Background app refresh + push notifications via APNs relay (if configured) |

The background service:
- Maintains the QUIC connection
- Subscribes to the event channel (stream `0x0400`)
- Delivers local notifications via the platform `INotificationService`
- Notification rules are configurable per camera and per event type

### iOS Limitation

iOS aggressively suspends background network connections. For reliable event notifications on iOS, the server would need to relay events through APNs (Apple Push Notification service). This requires a plugin on the server side and is not part of the core system. Without it, iOS notifications only work while the app is in the foreground or during brief background refresh windows.

## Re-enrollment

If the client needs to connect to a different server (or re-enroll with the same server after revocation or credential loss), the user can re-enroll from settings. This replaces the existing credentials. The client connects to one server at a time.

## Responsive Breakpoints

| Breakpoint | Width | Typical Devices | Gallery Columns | Navigation |
|------------|-------|-----------------|-----------------|------------|
| Compact | < 600px | Phones | 1 | Bottom bar |
| Medium | 600-1024px | Tablets, small laptops | 2-3 | Bottom bar or side rail |
| Expanded | > 1024px | Desktops, large tablets | 3-4+ (configurable) | Side rail |

The breakpoints are applied within each platform shell's layout containers. The shared controls adapt to available space.

## Credential Storage

See [enrollment.md](enrollment.md) for the per-platform secure storage table and credential format.
