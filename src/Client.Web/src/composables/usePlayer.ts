import { ref, watch, type Ref } from 'vue'
import { Fetcher } from '@/media/fetcher'
import { Decoder } from '@/media/decoder'
import { CanvasRenderer } from '@/media/canvasRenderer'
import { parseInitSegment, parseTimescale, demuxGop, type CodecConfig } from '@/media/fmp4'
import type { Streamer } from './useStreamer'

export interface Player {
  timestampUs: Ref<number>
  rate: Ref<number>
  direction: Ref<1 | -1>
  paused: Ref<boolean>
  buffering: Ref<boolean>
  blocked: Ref<boolean>
  mode: Ref<'live' | 'playback'>
  minRate: Ref<number>
  maxRate: Ref<number>

  attach: (container: HTMLElement, streamer: Streamer, cameraId: string, profile: string) => void
  detach: () => void
  seek: (timestampUs: number) => void
  scrubStart: () => void
  scrubMove: (timestampUs: number) => void
  scrubEnd: (timestampUs: number) => void
  setRate: (rate: number) => void
  togglePause: () => void
  goLive: () => void
  setProfile: (profile: string) => void
  stop: () => void
}

type State = 'idle' | 'seeking' | 'streaming'

export function usePlayer(): Player {
  const timestampUs = ref(0)
  const rate = ref(1)
  const direction = ref<1 | -1>(1)
  const paused = ref(false)
  const buffering = ref(false)
  const blocked = ref(false)
  const mode = ref<'live' | 'playback'>('live')
  const minRate = ref(-8)
  const maxRate = ref(8)

  let state: State = 'idle'
  let streamer: Streamer | null = null
  let currentProfile = ''
  let ignoreData = false

  const fetcher = new Fetcher()
  const decoder = new Decoder(fetcher)
  const renderer = new CanvasRenderer()

  let codecConfig: CodecConfig | null = null
  let timescale = 90000
  let rafId: number | null = null
  let lastTick = 0
  let accumUs = 0
  let lastFrameDurationUs = 40000
  let stride = 1

  let seekTargetUs = 0
  let seekRenderTarget = 0
  let seekBuffer: Uint8Array[] = []
  let seekGopTimestamps: number[] = []
  let seekGapEnd = 0
  let suppressBuffering = false

  const debug = typeof localStorage !== 'undefined' && localStorage.getItem('debug_player') !== null

  function sendGoLive() {
    streamer?.goLive(currentProfile)
  }

  function computeNeededGops(ts: number): number[] {
    const available = fetcher.gopTimestamps()
    const currentGopIdx = findGopIndex(available, ts)
    if (currentGopIdx < 0) return []

    const lookahead = Math.max(1, Math.floor(rate.value))
    const dir = direction.value
    const needed: number[] = []
    const behindIdx = currentGopIdx - dir
    if (behindIdx >= 0 && behindIdx < available.length)
      needed.push(available[behindIdx])
    for (let i = 0; i <= lookahead; i++) {
      const targetIdx = currentGopIdx + (i * dir)
      if (targetIdx < 0 || targetIdx >= available.length) break
      needed.push(available[targetIdx])
    }
    return needed
  }

  function updatePipeline(ts: number) {
    const windowUs = 30_000_000 * Math.max(1, rate.value)
    const dir = direction.value
    const fromUs = dir === 1 ? ts : ts + windowUs
    const toUs = dir === 1 ? ts + windowUs : ts - windowUs
    fetcher.setTarget(fromUs, toUs)
    decoder.setTarget(computeNeededGops(ts))
  }

  const liveCatchupMaxBoost = 0.1
  const liveCatchupTauUs = 4_000_000

  function liveCatchupMultiplier(): number {
    if (mode.value !== 'live') return 1
    const gops = fetcher.gopTimestamps()
    if (gops.length === 0) return 1
    const lag = gops[gops.length - 1] - timestampUs.value
    if (lag <= 0) return 1
    return 1 + liveCatchupMaxBoost * (1 - Math.exp(-lag / liveCatchupTauUs))
  }

  function renderAt(ts: number): boolean {
    updatePipeline(ts)
    const frame = decoder.getFrame(ts)
    if (!frame) return false
    renderer.renderFrame(frame.frame)
    timestampUs.value = frame.timestamp
    if (frame.duration > 0) lastFrameDurationUs = frame.duration
    return true
  }

  function findGopIndex(timestamps: number[], ts: number): number {
    if (timestamps.length === 0) return -1
    let lo = 0
    let hi = timestamps.length - 1
    while (lo < hi) {
      const mid = (lo + hi + 1) >>> 1
      if (timestamps[mid] <= ts)
        lo = mid
      else
        hi = mid - 1
    }
    return timestamps[lo] <= ts ? lo : -1
  }

  function startLoop() {
    if (rafId !== null) return
    rafId = requestAnimationFrame(loop)
  }

  function stopLoop() {
    if (rafId !== null) {
      cancelAnimationFrame(rafId)
      rafId = null
    }
  }

  function loop() {
    rafId = requestAnimationFrame(loop)

    if (state === 'seeking') {
      if (seekRenderTarget > 0) {
        if (renderAt(seekRenderTarget)) {
          seekRenderTarget = 0
          enterStreaming()
        }
      } else if (seekTargetUs > 0) {
        updatePipeline(seekTargetUs)
      }
      return
    }

    if (state !== 'streaming') return

    if (paused.value) return

    const now = performance.now()
    const elapsed = now - lastTick
    lastTick = now

    const effectiveDurationUs = lastFrameDurationUs * stride
    accumUs += elapsed * 1000 * rate.value * liveCatchupMultiplier()

    if (accumUs < effectiveDurationUs) return

    const steps = Math.floor(accumUs / effectiveDurationUs)
    accumUs -= steps * effectiveDurationUs
    const nextTs = timestampUs.value + steps * effectiveDurationUs * direction.value

    if (!renderAt(nextTs)) {
      if (!suppressBuffering)
        buffering.value = true
      accumUs = 0
      lastTick = performance.now()
      return
    }
    buffering.value = false
    suppressBuffering = false
  }

  function enterSeeking(ts?: number) {
    stopLoop()
    state = 'seeking'
    seekBuffer = []
    seekGopTimestamps = []
    seekGapEnd = 0
    seekTargetUs = ts ?? 0
    seekRenderTarget = 0
    ignoreData = true
    buffering.value = false
    fetcher.reset()
    decoder.flush()
    if (codecConfig) decoder.configure(codecConfig)
    startLoop()
  }

  function enterStreaming() {
    state = 'streaming'
    lastTick = performance.now()
    accumUs = 0
  }

  function commitSeek() {
    if (debug) console.log('commitSeek', 'chunks:', seekBuffer.length, 'target:', seekTargetUs)
    for (let i = 0; i < seekBuffer.length; i++)
      fetcher.appendData(seekGopTimestamps[i], seekBuffer[i])
    seekBuffer = []
    seekGopTimestamps = []
    seekRenderTarget = seekTargetUs
  }

  function commitLive() {
    let newestWallClock = 0
    let newestIdx = -1
    for (let i = 0; i < seekBuffer.length; i++) {
      const demuxed = demuxGop(seekBuffer[i], timescale)
      for (const sample of demuxed.samples) {
        if (sample.timestamp > newestWallClock) {
          newestWallClock = sample.timestamp
          newestIdx = i
        }
      }
    }

    if (newestWallClock === 0) return

    for (let i = newestIdx; i < seekBuffer.length; i++)
      fetcher.appendData(seekGopTimestamps[i], seekBuffer[i])
    const dropped = newestIdx
    seekBuffer = []
    seekGopTimestamps = []
    seekRenderTarget = newestWallClock
    if (debug) console.log('commitLive anchor', newestWallClock, 'droppedStale', dropped)
  }

  function handleAck() {
    ignoreData = false
  }

  function handleInit(_profile: string, data: Uint8Array) {
    if (ignoreData) return
    const newConfig = parseInitSegment(data)
    const newTimescale = parseTimescale(data)
    if (!newConfig) return
    timescale = newTimescale
    decoder.setTimescale(newTimescale)
    if (!codecConfig
        || codecConfig.codec !== newConfig.codec
        || codecConfig.width !== newConfig.width
        || codecConfig.height !== newConfig.height) {
      codecConfig = newConfig
      decoder.configure(codecConfig)
    }
  }

  function handleGop(_profile: string, gopTimestamp: number, data: Uint8Array) {
    if (ignoreData) return

    switch (state) {
      case 'idle':
        break

      case 'seeking':
        if (seekRenderTarget > 0) {
          fetcher.appendData(gopTimestamp, new Uint8Array(data))
        } else {
          seekBuffer.push(new Uint8Array(data))
          seekGopTimestamps.push(gopTimestamp)
          if (mode.value === 'live')
            commitLive()
          else if (seekBuffer.length === 1)
            commitSeek()
        }
        break

      case 'streaming':
        fetcher.appendData(gopTimestamp, new Uint8Array(data))
        break
    }
  }

  function handleGap(_from: number, to: number) {
    if (debug) console.log('GAP', new Date(_from / 1000).toISOString(), '->', new Date(to / 1000).toISOString(), 'state:', state)
    fetcher.handleGap(_from, to)
    switch (state) {
      case 'idle':
        break

      case 'seeking':
        seekGapEnd = to
        break

      case 'streaming':
        decoder.resetWallClock()
        if (direction.value === 1 && timestampUs.value < to)
          renderAt(to)
        else if (direction.value === -1 && timestampUs.value > _from)
          renderAt(_from)
        break
    }
  }

  function handleFetchComplete() {
    if (debug) console.log('FETCHCOMPLETE', 'state:', state, 'seekGapEnd:', seekGapEnd)
    fetcher.handleFetchComplete()
    if (state === 'seeking' && seekGapEnd > 0) {
      const from = seekGapEnd
      seekGapEnd = 0
      fetcher.setTarget(from, from + 30_000_000)
    }
  }

  function handleLive() {
    mode.value = 'live'
    minRate.value = 1
    maxRate.value = 1
    rate.value = 1
    direction.value = 1
    fetcher.handleLive()
  }

  function handleRecording() {
    mode.value = 'playback'
    minRate.value = -8
    maxRate.value = 8
    fetcher.handleRecording()
  }

  function seek(ts: number) {
    if (state === 'idle') return
    if (debug) console.log('USER SEEK', new Date(ts / 1000).toISOString(), 'state:', state)
    enterSeeking(ts)
  }

  function goLive() {
    if (debug) console.log('USER GOLIVE', 'state:', state)
    enterSeeking()
    paused.value = false
    sendGoLive()
  }

  function togglePause() {
    paused.value = !paused.value
    if (!paused.value) {
      lastTick = performance.now()
      accumUs = 0
    }
  }

  let scrubPendingTs = 0
  let scrubRunning = false

  function scrubStart() {
    paused.value = true
  }

  function scrubMove(ts: number) {
    scrubPendingTs = ts
    if (!scrubRunning) scrubLoop()
  }

  async function scrubLoop() {
    scrubRunning = true
    while (scrubPendingTs > 0 && paused.value) {
      const ts = scrubPendingTs
      scrubPendingTs = 0

      if (scrubRender(ts)) continue

      await fetcher.fetchAt(ts)
      scrubRender(ts)
    }
    scrubRunning = false
  }

  function scrubRender(ts: number): boolean {
    const frame = decoder.getFrame(ts)
    if (frame && Math.abs(frame.timestamp - ts) < 5_000_000) {
      renderer.renderFrame(frame.frame)
      timestampUs.value = frame.timestamp
      if (frame.duration > 0) lastFrameDurationUs = frame.duration
      return true
    }

    const gop = fetcher.findGop(ts)
    if (gop) {
      decoder.decodeKeyframe(fetcher.mergedData(gop), gop.timestamp)
      const kf = decoder.getFrame(ts)
      if (kf) {
        renderer.renderFrame(kf.frame)
        timestampUs.value = kf.timestamp
        if (kf.duration > 0) lastFrameDurationUs = kf.duration
        return true
      }
    }

    return false
  }

  function scrubEnd(ts: number) {
    scrubPendingTs = 0
    paused.value = false
    seek(ts)
  }

  function setRate(r: number) {
    if (mode.value === 'live') return
    const newDir = r < 0 ? -1 : 1
    const newRate = Math.abs(r)

    const dirChanged = newDir !== direction.value
    const rateChanged = newRate !== rate.value

    direction.value = newDir as 1 | -1
    rate.value = newRate

    if (dirChanged || rateChanged) {
      const newStride = newRate >= 3 ? Math.floor(newRate) : 1
      accumUs = 0
      if (newStride !== stride) {
        stride = newStride
        suppressBuffering = true
        decoder.setStride(newStride)
      }
    }
  }

  function setProfile(profile: string) {
    currentProfile = profile
    if (mode.value === 'live')
      goLive()
    else
      seek(timestampUs.value)
  }

  function attach(container: HTMLElement, s: Streamer, cameraId: string, profile: string) {
    const canvas = document.createElement('canvas')
    canvas.className = 'w-full'
    container.appendChild(canvas)
    renderer.attach(canvas)
    streamer = s
    currentProfile = profile
    fetcher.attach((from, to) => s.fetch(currentProfile, from, to))

    s.onAck = handleAck
    s.onInit = handleInit
    s.onGop = handleGop
    s.onGap = handleGap
    s.onFetchComplete = handleFetchComplete
    s.onLive = handleLive
    s.onRecording = handleRecording

    s.connect(cameraId)

    watch(s.status, (status) => {
      if (status === 'connected')
        goLive()
    })
  }

  function detach() {
    stopLoop()
    if (streamer) {
      streamer.onAck = null
      streamer.onInit = null
      streamer.onGop = null
      streamer.onFetchComplete = null
      streamer.onGap = null
      streamer.onLive = null
      streamer.onRecording = null
    }
    streamer = null
    fetcher.detach()
    renderer.detach()
  }

  function stop() {
    detach()
    fetcher.reset()
    decoder.flush()
    state = 'idle'
    timestampUs.value = 0
    rate.value = 1
    direction.value = 1
    paused.value = false
    buffering.value = false
    ignoreData = false
    codecConfig = null
  }

  return {
    timestampUs,
    rate,
    direction,
    paused,
    buffering,
    blocked,
    mode,
    minRate,
    maxRate,
    attach,
    detach,
    seek,
    scrubStart,
    scrubMove,
    scrubEnd,
    setRate,
    togglePause,
    goLive,
    setProfile,
    stop,
  }
}
