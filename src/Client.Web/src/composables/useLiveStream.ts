import { ref, onBeforeUnmount, type Ref } from 'vue'
import { api } from '@/api/client'

export interface LiveStreamState {
  status: Ref<'idle' | 'connecting' | 'streaming' | 'error' | 'blocked'>
  error: Ref<string>
  start: (videoEl: HTMLVideoElement) => void
  stop: () => void
}

export function useLiveStream(cameraId: Ref<string>, profile: Ref<string>): LiveStreamState {
  const status = ref<'idle' | 'connecting' | 'streaming' | 'error' | 'blocked'>('idle')
  const error = ref('')

  let ws: WebSocket | null = null
  let mediaSource: MediaSource | null = null
  let sourceBuffer: SourceBuffer | null = null
  let queue: ArrayBuffer[] = []
  let videoEl: HTMLVideoElement | null = null
  let abortController: AbortController | null = null
  let playAttempted = false

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
      }

      ws.onmessage = (ev: MessageEvent) => {
        queue.push(ev.data as ArrayBuffer)
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

  function stopStream() {
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

  return { status, error, start, stop }
}
