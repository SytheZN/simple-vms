import { ref, type Ref } from 'vue'
import { encodeLive, parseServerMessage, ServerMsg, Status } from '@/media/streamProtocol'
import type { CameraListItem, StreamProfile } from '@/types/api'

const RECONNECT_DELAY_MS = 5000

// MJPG magic, version, 8-byte timestamp, 4-byte payload length.
const FRAGMENT_HEADER_BYTES = 17
const FRAGMENT_MAGIC = [0x4d, 0x4a, 0x50, 0x47]

function unwrapFragment(fragment: Uint8Array): Uint8Array | null {
  if (fragment.length <= FRAGMENT_HEADER_BYTES) return null
  for (let i = 0; i < FRAGMENT_MAGIC.length; i++)
    if (fragment[i] !== FRAGMENT_MAGIC[i]) return null

  const view = new DataView(fragment.buffer, fragment.byteOffset)
  const length = view.getUint32(13, true)
  if (length === 0 || FRAGMENT_HEADER_BYTES + length > fragment.length) return null

  return fragment.subarray(FRAGMENT_HEADER_BYTES, FRAGMENT_HEADER_BYTES + length)
}

export interface GalleryThumbnails {
  urls: Ref<Record<string, string>>
  sync: (cameras: CameraListItem[]) => void
  stop: () => void
}

const THUMBNAIL_SUFFIX = '-thumbnail'

export function thumbnailStreams(camera: CameraListItem): StreamProfile[] {
  return camera.streams.filter(s =>
    s.kind === 'metadata'
    && s.profile.endsWith(THUMBNAIL_SUFFIX)
    && s.formatId === 'mjpeg')
}

export function useGalleryThumbnails(): GalleryThumbnails {
  const urls = ref<Record<string, string>>({})

  interface Subscription {
    profiles: string[]
    candidate: number
    socket: WebSocket | null
    retry: number | null
    closed: boolean
  }

  const subscriptions = new Map<string, Subscription>()

  function wsUrl(cameraId: string): string {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:'
    return `${proto}//${location.host}/api/v1/stream/${cameraId}`
  }

  function publish(cameraId: string, jpeg: Uint8Array) {
    const url = URL.createObjectURL(new Blob([jpeg as BlobPart], { type: 'image/jpeg' }))
    const previous = urls.value[cameraId]
    urls.value = { ...urls.value, [cameraId]: url }
    if (previous) URL.revokeObjectURL(previous)
  }

  function clearFrame(cameraId: string) {
    const url = urls.value[cameraId]
    if (!url) return

    const { [cameraId]: _dropped, ...rest } = urls.value
    urls.value = rest
    URL.revokeObjectURL(url)
  }

  function connect(cameraId: string, sub: Subscription) {
    const socket = new WebSocket(wsUrl(cameraId))
    socket.binaryType = 'arraybuffer'
    sub.socket = socket

    socket.onopen = () => socket.send(encodeLive(sub.profiles[sub.candidate]))

    socket.onmessage = (ev: MessageEvent) => {
      const msg = parseServerMessage(ev.data as ArrayBuffer)

      if (msg.type === ServerMsg.Status) {
        const code = (msg as { code: number }).code

        if (code === Status.Ended) {
          clearFrame(cameraId)
          socket.close()
          return
        }

        if (code !== Status.Error) return

        console.warn(
          `thumbnail stream '${sub.profiles[sub.candidate]}' unavailable on camera ${cameraId}`)
        sub.candidate++
        if (sub.candidate < sub.profiles.length) {
          socket.send(encodeLive(sub.profiles[sub.candidate]))
          return
        }

        clearFrame(cameraId)
        socket.close()
        return
      }

      if (msg.type !== ServerMsg.Gop) return

      const jpeg = unwrapFragment((msg as { data: Uint8Array }).data)
      if (jpeg) publish(cameraId, jpeg)
    }

    socket.onclose = () => {
      sub.socket = null
      clearFrame(cameraId)
      if (sub.closed) return
      sub.candidate = 0
      sub.retry = window.setTimeout(() => connect(cameraId, sub), RECONNECT_DELAY_MS)
    }
  }

  function unsubscribe(cameraId: string) {
    const sub = subscriptions.get(cameraId)
    if (!sub) return
    sub.closed = true
    if (sub.retry !== null) window.clearTimeout(sub.retry)
    sub.socket?.close()
    subscriptions.delete(cameraId)
    clearFrame(cameraId)
  }

  function sync(cameras: CameraListItem[]) {
    const wanted = new Map<string, string[]>()
    for (const camera of cameras) {
      const profiles = thumbnailStreams(camera).map(s => s.profile)
      if (profiles.length > 0) wanted.set(camera.id, profiles)
    }

    for (const cameraId of [...subscriptions.keys()]) {
      const current = subscriptions.get(cameraId)!.profiles.join()
      if (wanted.get(cameraId)?.join() !== current)
        unsubscribe(cameraId)
    }

    for (const [cameraId, profiles] of wanted) {
      if (subscriptions.has(cameraId)) continue
      const sub: Subscription = { profiles, candidate: 0, socket: null, retry: null, closed: false }
      subscriptions.set(cameraId, sub)
      connect(cameraId, sub)
    }
  }

  function stop() {
    for (const cameraId of [...subscriptions.keys()])
      unsubscribe(cameraId)
  }

  return { urls, sync, stop }
}
