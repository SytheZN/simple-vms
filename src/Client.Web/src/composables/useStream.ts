import { ref, type Ref } from 'vue'
import { api } from '@/api/client'

export interface WallClockSync {
  wallClockUs: number
  bufferedEndAtSync: number
}

export interface StreamState {
  status: Ref<'idle' | 'connecting' | 'streaming' | 'error' | 'blocked' | 'ended'>
  error: Ref<string>
  lagMs: Ref<number>
  wallClockSync: Ref<WallClockSync | null>
  mode: Ref<'live' | 'playback'>
  connect: (videoEl: HTMLVideoElement, from?: number, segmentId?: string) => Promise<void>
  stop: () => void
}

export function useStream(cameraId: Ref<string>, profile: Ref<string>): StreamState {
  const status = ref<'idle' | 'connecting' | 'streaming' | 'error' | 'blocked' | 'ended'>('idle')
  const error = ref('')
  const lagMs = ref(0)
  const wallClockSync = ref<WallClockSync | null>(null)
  const mode = ref<'live' | 'playback'>('live')

  let ws: WebSocket | null = null
  let lagTimer: ReturnType<typeof setInterval> | null = null
  let mediaSource: MediaSource | null = null
  let sourceBuffer: SourceBuffer | null = null
  let queue: { data: ArrayBuffer, wallClockUs: number }[] = []
  let videoEl: HTMLVideoElement | null = null
  let playAttempted = false
  let isPlayback = false
  let batchRemaining = 0
  let pausedForBuffer = false
  let consumedCount = 0
  let consumeRatePerSec = 25
  let lastConsumeCheck = 0

  const maxBufferSeconds = 10
  const bufferPauseThreshold = 2
  const bufferResumeThreshold = 4

  function extractPrftWallClock(data: ArrayBuffer): number {
    if (data.byteLength < 40) return 0
    const view = new DataView(data)

    const moofSize = view.getUint32(0)
    if (moofSize < 40 || moofSize > data.byteLength) return 0

    const prftOffset = moofSize - 32
    const prftType = String.fromCharCode(
      view.getUint8(prftOffset + 4), view.getUint8(prftOffset + 5),
      view.getUint8(prftOffset + 6), view.getUint8(prftOffset + 7))
    if (prftType !== 'prft') return 0

    return Number(view.getBigUint64(prftOffset + 16))
  }

  function wsUrl(from?: number, segmentId?: string): string {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:'
    if (from && segmentId)
      return `${proto}//${location.host}/api/v1/playback/${cameraId.value}/${profile.value}?from=${from}&segmentId=${segmentId}`
    return `${proto}//${location.host}/api/v1/live/${cameraId.value}/${profile.value}`
  }

  function requestBoxes(count: number) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return
    count = Math.min(count, 1024)
    const buf = new Uint8Array(2)
    buf[0] = (count >> 8) & 0xFF
    buf[1] = count & 0xFF
    ws.send(buf)
    batchRemaining += count
  }

  function maybeRequestMore() {
    if (!isPlayback) return
    const threshold = Math.ceil(consumeRatePerSec * 3)
    const batchSize = Math.min(Math.ceil(consumeRatePerSec * 5), 1024)
    if (batchRemaining < threshold)
      requestBoxes(batchSize)
  }

  let pendingWallClock = 0

  function onUpdateEnd() {
    consumedCount++
    if (pendingWallClock && videoEl && videoEl.buffered.length > 0) {
      wallClockSync.value = {
        wallClockUs: pendingWallClock,
        bufferedEndAtSync: videoEl.buffered.end(videoEl.buffered.length - 1)
      }
      pendingWallClock = 0
    }
    appendNext()
  }

  function updateConsumeRate() {
    const now = performance.now()
    if (lastConsumeCheck === 0) {
      lastConsumeCheck = now
      consumedCount = 0
      return
    }
    const elapsed = (now - lastConsumeCheck) / 1000
    if (elapsed >= 1) {
      consumeRatePerSec = consumedCount / elapsed
      consumedCount = 0
      lastConsumeCheck = now
    }
  }

  function appendNext() {
    if (!sourceBuffer || sourceBuffer.updating || queue.length === 0) return

    if (videoEl && videoEl.buffered.length > 0) {
      const buffered = videoEl.buffered.end(videoEl.buffered.length - 1) - videoEl.currentTime
      if (buffered > maxBufferSeconds) {
        setTimeout(appendNext, 500)
        return
      }
    }

    const entry = queue.shift()!
    if (entry.wallClockUs)
      pendingWallClock = entry.wallClockUs

    try {
      sourceBuffer.appendBuffer(entry.data)
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

  async function connect(el: HTMLVideoElement, from?: number, segmentId?: string) {
    stop()
    videoEl = el
    status.value = 'connecting'
    error.value = ''
    isPlayback = !!(from && segmentId)
    mode.value = isPlayback ? 'playback' : 'live'
    batchRemaining = 0

    let mimeType: string
    try {
      const metadata = await api.live.metadata(cameraId.value, profile.value)
      mimeType = metadata.mimeType
    } catch {
      error.value = 'Failed to fetch stream metadata'
      status.value = 'error'
      return
    }

    if (!MediaSource.isTypeSupported(mimeType)) {
      error.value = `Browser does not support ${mimeType}`
      status.value = 'error'
      return
    }

    const debug = import.meta.env.DEV
    mediaSource = new MediaSource()
    if (debug) {
      mediaSource.addEventListener('sourceended', () => console.log('MediaSource sourceended'))
      mediaSource.addEventListener('sourceclose', () => console.log('MediaSource sourceclose'))
    }
    el.src = URL.createObjectURL(mediaSource)

    let msgCount = 0
    mediaSource.addEventListener('sourceopen', () => {
      if (!mediaSource || mediaSource.readyState !== 'open') return
      try {
        sourceBuffer = mediaSource.addSourceBuffer(mimeType)
        sourceBuffer.mode = 'sequence'
        sourceBuffer.addEventListener('updateend', onUpdateEnd)
        if (debug)
          sourceBuffer.addEventListener('error', (e) => console.error('SourceBuffer error', e))
      } catch (e) {
        error.value = `Failed to create source buffer: ${e}`
        status.value = 'error'
        stop()
        return
      }

      ws = new WebSocket(wsUrl(from, segmentId))
      ws.binaryType = 'arraybuffer'

      ws.onopen = () => {
        status.value = 'streaming'
        startLagMonitor()
        if (isPlayback) requestBoxes(90)
      }

      ws.onmessage = (ev: MessageEvent) => {
        const data = ev.data as ArrayBuffer
        msgCount++
        if (isPlayback) batchRemaining--
        if (debug && msgCount <= 3) {
          const view = new DataView(data)
          const boxType = data.byteLength >= 8
            ? String.fromCharCode(view.getUint8(4), view.getUint8(5), view.getUint8(6), view.getUint8(7))
            : '????'
          console.log(`WS msg #${msgCount}: ${data.byteLength} bytes, first box: ${boxType}`)
        }
        queue.push({ data, wallClockUs: extractPrftWallClock(data) })
        appendNext()
        maybeRequestMore()
      }

      ws.onclose = (ev: CloseEvent) => {
        if (debug)
          console.log(`WS closed: code=${ev.code} reason=${ev.reason} msgCount=${msgCount}`)
        if (status.value === 'streaming')
          status.value = isPlayback ? 'ended' : 'idle'
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
      updateConsumeRate()
      if (!videoEl || !videoEl.buffered.length) return
      const bufferedEnd = videoEl.buffered.end(videoEl.buffered.length - 1)
      const ahead = bufferedEnd - videoEl.currentTime
      lagMs.value = Math.round(ahead * 1000)

      if (!pausedForBuffer && ahead < bufferPauseThreshold && !videoEl.paused) {
        pausedForBuffer = true
        videoEl.pause()
      } else if (pausedForBuffer && ahead >= bufferResumeThreshold) {
        pausedForBuffer = false
        videoEl.play().catch(() => {})
      }
    }, 250)
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
    batchRemaining = 0
  }

  function stop() {
    stopStream()
    if (sourceBuffer) {
      sourceBuffer.removeEventListener('updateend', onUpdateEnd)
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
    wallClockSync.value = null
    status.value = 'idle'
  }

  return { status, error, lagMs, wallClockSync, mode, connect, stop }
}
