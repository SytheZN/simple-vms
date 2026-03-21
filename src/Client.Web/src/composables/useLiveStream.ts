import { ref, onBeforeUnmount, type Ref } from 'vue'
import { api } from '@/api/client'

export interface WallClockSync {
  wallClockUs: number
  presentationTimeSec: number
}

export interface LiveStreamState {
  status: Ref<'idle' | 'connecting' | 'streaming' | 'error' | 'blocked'>
  error: Ref<string>
  lagMs: Ref<number>
  wallClockSync: Ref<WallClockSync | null>
  start: (videoEl: HTMLVideoElement) => void
  stop: () => void
}

export function useLiveStream(cameraId: Ref<string>, profile: Ref<string>): LiveStreamState {
  const status = ref<'idle' | 'connecting' | 'streaming' | 'error' | 'blocked'>('idle')
  const error = ref('')
  const lagMs = ref(0)
  const wallClockSync = ref<WallClockSync | null>(null)

  let ws: WebSocket | null = null
  let lagTimer: ReturnType<typeof setInterval> | null = null
  let timescale = 90000
  let mediaSource: MediaSource | null = null
  let sourceBuffer: SourceBuffer | null = null
  let queue: ArrayBuffer[] = []
  let videoEl: HTMLVideoElement | null = null
  let abortController: AbortController | null = null
  let playAttempted = false

  const EMSG_SCHEME = 'urn:vms:wallclock'

  function parseEmsg(data: ArrayBuffer): boolean {
    if (data.byteLength < 12) return false
    const view = new DataView(data)
    const boxSize = view.getUint32(0)
    if (boxSize > data.byteLength) return false
    const boxType = String.fromCharCode(
      view.getUint8(4), view.getUint8(5), view.getUint8(6), view.getUint8(7))
    if (boxType !== 'emsg') return false

    const version = view.getUint8(8)
    if (version !== 1) return false

    timescale = view.getUint32(12)
    const presentationTime = Number(view.getBigUint64(16))
    const presentationTimeSec = presentationTime / timescale

    let offset = 32
    const bytes = new Uint8Array(data)

    // skip scheme_uri (null-terminated)
    const schemeStart = offset
    while (offset < boxSize && bytes[offset] !== 0) offset++
    const scheme = String.fromCharCode(...bytes.slice(schemeStart, offset))
    offset++ // skip null

    if (scheme !== EMSG_SCHEME) return false

    // skip value (null-terminated)
    while (offset < boxSize && bytes[offset] !== 0) offset++
    offset++

    if (offset + 8 > boxSize) return false
    const payloadView = new DataView(data, offset, 8)
    const wallClockUs = Number(payloadView.getBigUint64(0))

    wallClockSync.value = { wallClockUs, presentationTimeSec }
    return true
  }

  function wsUrl(): string {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:'
    return `${proto}//${location.host}/api/v1/live/${cameraId.value}/${profile.value}`
  }

  function appendNext() {
    if (!sourceBuffer || sourceBuffer.updating || queue.length === 0) return
    try {
      sourceBuffer.appendBuffer(queue.shift()!)
      if (!playAttempted && videoEl) {
        playAttempted = true
        videoEl.muted = true
        videoEl.play().catch(() => {
          setTimeout(() => {
            stopStream()
            status.value = 'blocked'
          }, 1000)
        })
      }
    } catch {
      error.value = 'Buffer append failed'
      status.value = 'error'
      stop()
    }
  }

  async function start(el: HTMLVideoElement) {
    stop()
    videoEl = el
    status.value = 'connecting'
    error.value = ''
    abortController = new AbortController()

    let mimeType: string
    try {
      const metadata = await api.live.metadata(cameraId.value, profile.value)
      mimeType = metadata.mimeType
    } catch {
      error.value = 'Failed to fetch stream metadata'
      status.value = 'error'
      return
    }

    if (abortController.signal.aborted) return

    if (!MediaSource.isTypeSupported(mimeType)) {
      error.value = `Browser does not support ${mimeType}`
      status.value = 'error'
      return
    }

    mediaSource = new MediaSource()
    el.src = URL.createObjectURL(mediaSource)

    const controller = abortController
    mediaSource.addEventListener('sourceopen', () => {
      if (controller?.signal.aborted) return
      try {
        sourceBuffer = mediaSource!.addSourceBuffer(mimeType)
        sourceBuffer.mode = 'segments'
        sourceBuffer.addEventListener('updateend', appendNext)
      } catch (e) {
        error.value = `Failed to create source buffer: ${e}`
        status.value = 'error'
        stop()
        return
      }

      ws = new WebSocket(wsUrl())
      ws.binaryType = 'arraybuffer'

      ws.onopen = () => {
        status.value = 'streaming'
        startLagMonitor()
      }

      ws.onmessage = (ev: MessageEvent) => {
        const data = ev.data as ArrayBuffer
        parseEmsg(data)
        queue.push(data)
        appendNext()
      }

      ws.onclose = () => {
        if (status.value === 'streaming')
          status.value = 'idle'
      }

      ws.onerror = () => {
        error.value = 'WebSocket connection failed'
        status.value = 'error'
      }
    })
  }

  function startLagMonitor() {
    stopLagMonitor()
    lagTimer = setInterval(() => {
      if (!videoEl || !videoEl.buffered.length) return
      const bufferedEnd = videoEl.buffered.end(videoEl.buffered.length - 1)
      lagMs.value = Math.round((bufferedEnd - videoEl.currentTime) * 1000)
    }, 1000)
  }

  function stopLagMonitor() {
    if (lagTimer) {
      clearInterval(lagTimer)
      lagTimer = null
    }
    lagMs.value = 0
  }

  function stopStream() {
    stopLagMonitor()
    if (ws) {
      ws.onclose = null
      ws.onerror = null
      ws.onmessage = null
      ws.close()
      ws = null
    }
    queue = []
    playAttempted = false
  }

  function stop() {
    abortController?.abort()
    abortController = null
    stopStream()
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
    if (videoEl) {
      URL.revokeObjectURL(videoEl.src)
      videoEl.src = ''
      videoEl = null
    }
    status.value = 'idle'
  }

  onBeforeUnmount(stop)

  return { status, error, lagMs, wallClockSync, start, stop }
}
