# Client.Web

## Debug flags

Flags are read from `localStorage` at page load. Set them in the browser console
and reload; the value doesn't matter, only the key's presence.

```js
localStorage.setItem('debug_player', '1')   // enable
localStorage.removeItem('debug_player')     // disable
```

| Key | Effect |
|---|---|
| `debug_player` | Console logging for the streaming stack: WebSocket lifecycle, init segment parsing, status messages, seek/go-live transitions, buffering enter/exit, overlay source selection. Per-frame events (GOP receipt, overlay paints) are not logged; use the stats panel for those. |
| `debug_blank_video` | Renders a black layer over the video (under the motion overlay) so overlay output can be judged without the image behind it. |
| `debug_force_mse` | Skips WebCodecs detection and forces the MSE fallback player. |

## Stats panel

`Ctrl+D` on the camera view toggles a diagnostics panel over the video
(mirrors the native client's overlay, same shortcut): backend, player state,
rate and live catch-up, buffer depth, playhead position, fetcher and decoder
counters, frame timing (last/avg/min/max, fps), motion overlay status, and a
color-coded frame-time graph. Independent of `debug_player`.

On the MSE fallback the frame-time graph requires `requestVideoFrameCallback`
support; browsers without it show an empty graph.

## Other localStorage keys

| Key | Effect |
|---|---|
| `theme-preference` | `light` or `dark`; absent means follow the system theme. Managed by the theme toggle in the UI. |
