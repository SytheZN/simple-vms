# SimpleVms.FFmpeg.Native

Native FFmpeg decode libraries and generated C# bindings for SimpleVMS clients.

## Contents

- **Native shared libraries** for linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64, android-arm64, android-x64, and iOS (static xcframework)
- **Generated C# P/Invoke bindings** compiled into your project as content files
- **FFmpeg headers** under `build/native/include/` for reference

## Included FFmpeg components

Only the minimal decode subset is built (`--disable-everything` base):

- `libavcodec` - H.264, HEVC, and MJPEG decoders
- `libavutil` - pixel formats, frame management, hardware contexts
- `libswscale` - pixel format conversion

## Hardware acceleration

Platform-specific hardware decoders are enabled per target:

| Platform          | HW accel               |
| ----------------- | ---------------------- |
| Linux x64         | VAAPI, VDPAU, Vulkan   |
| Linux arm64       | Vulkan                 |
| Windows x64/arm64 | D3D11VA, DXVA2, Vulkan |
| macOS x64/arm64   | VideoToolbox           |
| iOS               | VideoToolbox (static)  |
| Android arm64/x64 | MediaCodec, Vulkan     |

## Usage

Add the package to your project:

```xml
<PackageReference Include="SimpleVms.FFmpeg.Native" />
```

The bindings are included as content files and compile into your assembly. Native libraries are resolved automatically per platform at runtime.

Three method classes, each importing from the correct native library:

- `FFAvUtil` - `libavutil` functions (`av_frame_alloc`, `av_hwdevice_ctx_create`, etc.)
- `FFAvCodec` - `libavcodec` functions (`avcodec_find_decoder`, `avcodec_send_packet`, etc.)
- `FFSwScale` - `libswscale` functions (`sws_getContext`, `sws_scale`, etc.)

All FFmpeg struct types (`AVCodecContext`, `AVFrame`, `AVPacket`, etc.) are generated with correct field layouts matching the bundled FFmpeg version. Requires `AllowUnsafeBlocks`.
