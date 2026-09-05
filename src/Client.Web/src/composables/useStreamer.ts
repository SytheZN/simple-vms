import { ref, type Ref } from 'vue'
import {
  encodeLive, encodeFetch, parseServerMessage,
  ServerMsg, Status
} from '@/media/streamProtocol'

export type StreamerStatus = 'idle' | 'connecting' | 'connected' | 'error'

export interface Streamer {
  status: Ref<StreamerStatus>
  error: Ref<string>
  connect: (cameraId: string) => void
  goLive: (profile: string) => void
  fetch: (profile: string, from: number, to: number) => void
  disconnect: () => void
  onInit: ((profile: string, data: Uint8Array) => void) | null
  onGop: ((profile: string, timestamp: number, data: Uint8Array, flags: number) => void) | null
  onAck: (() => void) | null
  onFetchComplete: (() => void) | null
  onGap: ((from: number, to: number) => void) | null
  onLive: (() => void) | null
  onRecording: (() => void) | null
}

export function useStreamer(): Streamer {
  const status = ref<StreamerStatus>('idle')
  const error = ref('')

  let ws: WebSocket | null = null
  const debug = typeof localStorage !== 'undefined' && localStorage.getItem('debug_player') !== null

  const streamer: Streamer = {
    status,
    error,
    connect,
    goLive,
    fetch: fetchRange,
    disconnect,
    onInit: null,
    onGop: null,
    onAck: null,
    onFetchComplete: null,
    onGap: null,
    onLive: null,
    onRecording: null,
  }

  function wsUrl(cameraId: string): string {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:'
    return `${proto}//${location.host}/api/v1/stream/${cameraId}`
  }

  function send(data: ArrayBuffer) {
    if (ws && ws.readyState === WebSocket.OPEN)
      ws.send(data)
  }

  function connect(cameraId: string) {
    disconnect()
    status.value = 'connecting'
    error.value = ''

    ws = new WebSocket(wsUrl(cameraId))
    ws.binaryType = 'arraybuffer'

    ws.onopen = () => {
      if (debug) console.log('streamer WS open', cameraId)
      status.value = 'connected'
    }

    ws.onmessage = (ev: MessageEvent) => {
      try {
        handleMessage(ev.data as ArrayBuffer)
      } catch (e) {
        console.error('streamer RX message failed', e)
      }
    }

    ws.onclose = (ev: CloseEvent) => {
      console.warn('streamer WS closed', ev.code, ev.reason || '')
      if (status.value === 'connected')
        status.value = 'idle'
    }

    ws.onerror = () => {
      console.error('streamer WS error')
      error.value = 'WebSocket connection failed'
      status.value = 'error'
    }
  }

  function handleMessage(buf: ArrayBuffer) {
    const msg = parseServerMessage(buf)

    if (msg.type === ServerMsg.Init) {
      const init = msg as { profile: string, data: Uint8Array }
      if (debug) console.log('streamer RX init', init.profile, init.data.length, 'bytes')
      streamer.onInit?.(init.profile, init.data)
      return
    }

    if (msg.type === ServerMsg.Gop) {
      const gop = msg as { flags: number, profile: string, timestamp: number, data: Uint8Array }
      streamer.onGop?.(gop.profile, gop.timestamp, gop.data, gop.flags)
      return
    }

    if (msg.type === ServerMsg.Status) {
      const st = msg as { code: number, gapFrom?: number, gapTo?: number }
      const statusNames: Record<number, string> = {
        [Status.Ack]: 'Ack', [Status.FetchComplete]: 'FetchComplete',
        [Status.Gap]: 'Gap', [Status.Error]: 'Error',
        [Status.Live]: 'Live', [Status.Recording]: 'Recording',
      }
      if (debug) {
        if (st.gapFrom !== undefined)
          console.log('streamer RX status', statusNames[st.code] ?? st.code, `gap=${st.gapFrom}-${st.gapTo}`)
        else
          console.log('streamer RX status', statusNames[st.code] ?? st.code)
      }
      if (st.code === Status.Ack) {
        streamer.onAck?.()
      } else if (st.code === Status.FetchComplete) {
        streamer.onFetchComplete?.()
      } else if (st.code === Status.Gap && st.gapFrom !== undefined && st.gapTo !== undefined) {
        streamer.onGap?.(st.gapFrom, st.gapTo)
      } else if (st.code === Status.Live) {
        streamer.onLive?.()
      } else if (st.code === Status.Recording) {
        streamer.onRecording?.()
      } else if (st.code === Status.Error) {
        console.error('streamer RX status Error')
        error.value = 'Stream error'
        status.value = 'error'
      }
    }
  }

  function goLive(profile: string) {
    if (debug) console.log('streamer TX goLive', profile)
    send(encodeLive(profile))
  }

  function fetchRange(profile: string, from: number, to: number) {
    if (debug) console.log('streamer TX fetch', profile, from, to)
    send(encodeFetch(profile, from, to))
  }

  function disconnect() {
    if (ws) {
      ws.onclose = null
      ws.onerror = null
      ws.onmessage = null
      ws.close()
      ws = null
    }
    status.value = 'idle'
  }

  return streamer
}
