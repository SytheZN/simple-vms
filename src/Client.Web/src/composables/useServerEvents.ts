import { ref, type Ref } from 'vue'
import type { LiveEvent } from '@/types/api'

const RECONNECT_DELAY_MS = 5000

export interface ServerEvents {
  connected: Ref<boolean>
  start: () => void
  stop: () => void
}

export function useServerEvents(onEvent: (event: LiveEvent) => void): ServerEvents {
  const connected = ref(false)

  let socket: WebSocket | null = null
  let retry: number | null = null
  let stopped = false

  function wsUrl(): string {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:'
    return `${proto}//${location.host}/api/v1/events/stream`
  }

  function connect() {
    socket = new WebSocket(wsUrl())

    socket.onopen = () => {
      connected.value = true
    }

    socket.onmessage = (ev: MessageEvent) => {
      try {
        onEvent(JSON.parse(ev.data as string) as LiveEvent)
      } catch {
        console.warn('discarding unparseable server event')
      }
    }

    socket.onclose = () => {
      socket = null
      connected.value = false
      if (stopped) return
      retry = window.setTimeout(connect, RECONNECT_DELAY_MS)
    }
  }

  function start() {
    if (socket || retry !== null) return
    stopped = false
    connect()
  }

  function stop() {
    stopped = true
    if (retry !== null) {
      window.clearTimeout(retry)
      retry = null
    }
    socket?.close()
    socket = null
    connected.value = false
  }

  return { connected, start, stop }
}
