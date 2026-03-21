import { ref, onUnmounted, type Ref } from 'vue'

export interface PlaybackStreamState {
  status: Ref<'idle' | 'connecting' | 'streaming' | 'error' | 'ended'>
  error: Ref<string>
  start: (videoEl: HTMLVideoElement, from: number, to?: number) => void
  seek: (timestamp: number) => void
  stop: () => void
}

export function usePlaybackStream(cameraId: Ref<string>, profile: Ref<string>): PlaybackStreamState {
  const status = ref<'idle' | 'connecting' | 'streaming' | 'error' | 'ended'>('idle')
  const error = ref('')

  let ws: WebSocket | null = null
  let mediaSource: MediaSource | null = null
  let sourceBuffer: SourceBuffer | null = null
  let queue: ArrayBuffer[] = []
  let videoEl: HTMLVideoElement | null = null

  function wsUrl(from: number, to?: number): string {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:'
    let url = `${proto}//${location.host}/api/v1/playback/${cameraId.value}/${profile.value}?from=${from}`
    if (to !== undefined) url += `&to=${to}`
    return url
  }

  function appendNext() {
    if (!sourceBuffer || sourceBuffer.updating || queue.length === 0) return
    const buf = queue.shift()!
    try {
      sourceBuffer.appendBuffer(buf)
    } catch {
      error.value = 'Buffer append failed'
      status.value = 'error'
      stop()
    }
  }

  function start(el: HTMLVideoElement, from: number, to?: number) {
    stop()
    videoEl = el
    status.value = 'connecting'
    error.value = ''

    mediaSource = new MediaSource()
    el.src = URL.createObjectURL(mediaSource)

    mediaSource.addEventListener('sourceopen', () => {
      connect(from, to)
    })
  }

  function connect(from: number, to?: number) {
    if (!mediaSource || mediaSource.readyState !== 'open') return

    ws = new WebSocket(wsUrl(from, to))
    ws.binaryType = 'arraybuffer'

    ws.onopen = () => {
      status.value = 'streaming'
    }

    ws.onmessage = (ev: MessageEvent) => {
      const data = ev.data as ArrayBuffer
      if (!sourceBuffer) {
        try {
          sourceBuffer = mediaSource!.addSourceBuffer('video/mp4; codecs="avc1.640029"')
          sourceBuffer.mode = 'segments'
          sourceBuffer.addEventListener('updateend', appendNext)
        } catch (e) {
          error.value = `Failed to create source buffer: ${e}`
          status.value = 'error'
          stop()
          return
        }
      }

      queue.push(data)
      appendNext()

      if (videoEl && videoEl.paused) {
        videoEl.play().catch(() => {})
      }
    }

    ws.onclose = () => {
      if (status.value === 'streaming') {
        status.value = 'ended'
      }
    }

    ws.onerror = () => {
      error.value = 'Playback connection failed'
      status.value = 'error'
    }
  }

  function seek(timestamp: number) {
    if (!videoEl) return
    const el = videoEl
    cleanup()
    start(el, timestamp)
  }

  function cleanup() {
    if (ws) {
      ws.onclose = null
      ws.onerror = null
      ws.onmessage = null
      ws.close()
      ws = null
    }
    if (sourceBuffer) {
      sourceBuffer.removeEventListener('updateend', appendNext)
      sourceBuffer = null
    }
    if (mediaSource) {
      if (mediaSource.readyState === 'open') {
        try { mediaSource.endOfStream() } catch {}
      }
      mediaSource = null
    }
    queue = []
  }

  function stop() {
    cleanup()
    if (videoEl) {
      URL.revokeObjectURL(videoEl.src)
      videoEl.src = ''
      videoEl = null
    }
    status.value = 'idle'
  }

  onUnmounted(stop)

  return { status, error, start, seek, stop }
}
