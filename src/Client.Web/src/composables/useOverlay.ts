import { ref, computed, watch, type Ref } from 'vue'
import { api } from '@/api/client'
import { Fetcher } from '@/media/fetcher'
import { MotionDecoder, type MotionFrame } from '@/media/motionDecoder'
import { computeNeededGops } from '@/media/decodeController'
import type { PipelineStats } from './usePlayer'
import { GopFlag } from '@/media/streamProtocol'
import { useStreamer } from './useStreamer'
import type { CameraListItem, StreamProfile } from '@/types/api'

export interface OverlayPlayerView {
  timestampUs: number
  rate: number
  direction: 1 | -1
  paused: boolean
  mode: 'live' | 'playback'
}

const holdLimitUs = 5_000_000
const windowBaseUs = 30_000_000
const nullResolveRetryMs = 2_000
const motionSuffix = '-motion-grid'

export function useOverlay<T extends OverlayPlayerView>(
  cameraId: Ref<string>,
  camera: Ref<CameraListItem | null>,
  videoProfile: Ref<string>,
  canvasRef: Ref<HTMLCanvasElement | null>,
  playerState: Ref<T>,
) {
  const debug = typeof localStorage !== 'undefined' && localStorage.getItem('debug_player') !== null

  const active = ref(false)
  const sourceProfile = ref<string | null>(null)

  const streamer = useStreamer()
  const fetcher = new Fetcher()
  const decoder = new MotionDecoder(fetcher)

  const triedLive = new Set<string>()
  let lastPaintedTs = -1
  let resolvedFrom = 0
  let resolvedUntil = 0
  let resolving = false
  let lastNullResolveAt = 0

  function candidates(): StreamProfile[] {
    const all = camera.value?.streams.filter(s => s.kind === 'metadata' && s.codec === 'mgrd') ?? []
    const matching = videoProfile.value + motionSuffix
    return all.sort((a, b) => {
      const aMatch = a.profile === matching ? 0 : 1
      const bMatch = b.profile === matching ? 0 : 1
      if (aMatch !== bMatch) return aMatch - bMatch
      return a.profile.localeCompare(b.profile)
    })
  }

  const available = computed(() => candidates().length > 0)

  fetcher.attach((from, to) => {
    if (sourceProfile.value)
      streamer.fetch(sourceProfile.value, from, to)
  })

  streamer.onGop = (profile, timestamp, data, flags) => {
    if (!active.value || profile !== sourceProfile.value) return
    fetcher.appendData(timestamp, data, (flags & GopFlag.Begin) !== 0)
    onTick()
  }
  streamer.onFetchComplete = () => fetcher.handleFetchComplete()
  streamer.onGap = (from, to) => fetcher.handleGap(from, to)
  streamer.onLive = () => fetcher.handleLive()
  streamer.onRecording = () => fetcher.handleRecording()

  watch(streamer.status, (status) => {
    if (status === 'connected' && active.value)
      startForMode()
  })

  watch(streamer.error, (message) => {
    if (message !== 'Stream error' || !active.value) return
    if (playerState.value.mode !== 'live') return
    if (debug) console.log('overlay live source rejected, advancing past', sourceProfile.value)
    subscribeNextLive()
  })

  watch(available, (ok) => {
    if (!ok && active.value) deactivate()
  })

  watch(videoProfile, () => {
    if (!active.value) return
    resetData()
    startForMode()
  })

  watch(() => playerState.value.mode, () => {
    if (!active.value) return
    resetData()
    startForMode()
  })

  watch(() => playerState.value.timestampUs, onTick)

  function onTick() {
    if (!active.value) return
    const ps = playerState.value

    if (ps.timestampUs > 0) {
      if (ps.mode === 'playback' && !ps.paused)
        ensureResolved(ps)
      if (sourceProfile.value) {
        if (!ps.paused) {
          const windowUs = windowBaseUs * Math.max(1, ps.rate)
          const from = ps.direction === 1 ? ps.timestampUs : ps.timestampUs + windowUs
          const to = ps.direction === 1 ? ps.timestampUs + windowUs : ps.timestampUs - windowUs
          fetcher.setTarget(from, to)
        }
        decoder.setTarget(computeNeededGops(
          fetcher.gopTimestamps(), ps.timestampUs, ps.rate, ps.direction))
      }
    }

    paintAt(ps)
  }

  function ensureResolved(ps: OverlayPlayerView) {
    const inSpan = ps.timestampUs >= resolvedFrom && ps.timestampUs <= resolvedUntil
    if (inSpan && sourceProfile.value) return
    if (inSpan && Date.now() - lastNullResolveAt < nullResolveRetryMs) return
    if (resolving) return
    resolving = true

    const windowUs = windowBaseUs * Math.max(1, ps.rate)
    const lo = ps.direction === 1 ? ps.timestampUs : ps.timestampUs - windowUs
    const hi = ps.direction === 1 ? ps.timestampUs + windowUs : ps.timestampUs

    resolveSource(lo, hi)
      .then((profile) => {
        resolvedFrom = lo
        resolvedUntil = hi
        if (profile === null) lastNullResolveAt = Date.now()
        setSource(profile)
      })
      .catch(e => console.error('overlay resolve failed', e))
      .finally(() => { resolving = false })
  }

  async function resolveSource(lo: number, hi: number): Promise<string | null> {
    for (const candidate of candidates()) {
      const timeline = await api.recordings.timeline(cameraId.value, lo, hi, candidate.profile)
      if (timeline.spans.some(s => s.endTime > lo && s.startTime < hi))
        return candidate.profile
    }
    return null
  }

  function setSource(profile: string | null) {
    if (profile === sourceProfile.value) return
    if (debug) console.log('overlay source ->', profile ?? 'none')
    sourceProfile.value = profile
    fetcher.reset()
    decoder.flush()
  }

  function startForMode() {
    if (streamer.status.value !== 'connected') {
      streamer.connect(cameraId.value)
      return
    }
    if (playerState.value.mode === 'live') {
      triedLive.clear()
      subscribeNextLive()
    } else {
      resolvedFrom = 0
      resolvedUntil = 0
      onTick()
    }
  }

  function subscribeNextLive() {
    const candidate = candidates().find(c => !triedLive.has(c.profile)) ?? null
    sourceProfile.value = candidate?.profile ?? null
    fetcher.reset()
    decoder.flush()
    if (!candidate) {
      if (debug) console.log('overlay: no live source accepted, turning off')
      deactivate()
      return
    }
    triedLive.add(candidate.profile)
    streamer.error.value = ''
    streamer.goLive(candidate.profile)
  }

  function paintAt(ps: OverlayPlayerView) {
    const frame = decoder.getFrame(ps.timestampUs)
    if (!frame || Math.abs(ps.timestampUs - frame.timestamp) > holdLimitUs) {
      if (lastPaintedTs >= 0) clearCanvas()
      return
    }
    if (frame.timestamp === lastPaintedTs) return
    if (drawGrid(frame))
      lastPaintedTs = frame.timestamp
  }

  function clearCanvas() {
    lastPaintedTs = -1
    const canvas = canvasRef.value
    if (canvas) canvas.getContext('2d')?.clearRect(0, 0, canvas.width, canvas.height)
  }

  function drawGrid(frame: MotionFrame): boolean {
    const canvas = canvasRef.value
    if (!canvas) return false
    const { cells, cols, rows } = frame
    if (canvas.width !== cols || canvas.height !== rows) {
      canvas.width = cols
      canvas.height = rows
    }
    const ctx = canvas.getContext('2d')!
    ctx.clearRect(0, 0, cols, rows)
    ctx.fillStyle = getComputedStyle(document.documentElement).getPropertyValue('--color-motion-active').trim()
    for (let row = 0; row < rows; row++) {
      for (let col = 0; col < cols; col++) {
        const value = cells[row * cols + col]
        if (value === 0) continue
        ctx.globalAlpha = value / 255
        ctx.fillRect(col, row, 1, 1)
      }
    }
    ctx.globalAlpha = 1
    return true
  }

  function resetData() {
    fetcher.reset()
    decoder.flush()
    resolvedFrom = 0
    resolvedUntil = 0
    clearCanvas()
  }

  function toggle() {
    if (active.value) {
      deactivate()
    } else {
      if (debug) console.log('overlay on')
      active.value = true
      startForMode()
    }
  }

  function deactivate() {
    if (debug) console.log('overlay off')
    active.value = false
    sourceProfile.value = null
    streamer.disconnect()
    resetData()
  }

  function onSeek(_ts: number) {
    if (!active.value) return
    resetData()
  }

  function onGoLive() {
    if (!active.value) return
    resetData()
    startForMode()
  }

  function pipelineStats(): PipelineStats | null {
    if (!active.value) return null
    const f = fetcher.stats()
    const d = decoder.stats()
    const gopTs = fetcher.gopTimestamps()
    const pos = playerState.value.timestampUs
    return {
      label: 'overlay',
      profile: sourceProfile.value ?? '--',
      bufferUs: gopTs.length > 0 ? Math.max(0, gopTs[gopTs.length - 1] - pos) : 0,
      positionUs: pos,
      fetcherGops: f.gops,
      fetcherBytes: f.bytes,
      decoderGops: d.gops,
      decoderFrames: d.frames,
    }
  }

  return {
    active,
    available,
    sourceProfile,
    toggle,
    onSeek,
    onGoLive,
    deactivate,
    pipelineStats,
  }
}
