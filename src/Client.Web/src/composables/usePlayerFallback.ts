import { ref, watch } from 'vue'
import type { Player } from './usePlayer'
import type { Streamer } from './useStreamer'
import { parseInitSegment, buildCodecString } from '@/media/fmp4'

type State = 'blocked' | 'seeking' | 'streaming' | 'buffering-next'

interface MseSlot {
  video: HTMLVideoElement
  mediaSource: MediaSource | null
  sourceBuffer: SourceBuffer | null
  initData: Uint8Array | null
  initAppended: boolean
  queue: Uint8Array[]
  appending: boolean
  anchors: { mediaTimeS: number, wallClockUs: number }[]
  bufferEndS: number
  pendingWallClock: number
}

export function usePlayerFallback(): Player {
  const timestampUs = ref(0)
  const rate = ref(1)
  const direction = ref<1 | -1>(1)
  const paused = ref(false)
  const buffering = ref(false)
  const blocked = ref(false)
  const mode = ref<'live' | 'playback'>('live')
  const minRate = ref(0.25)
  const maxRate = ref(5)

  let state: State = 'blocked'
  let container: HTMLElement | null = null
  let streamer: Streamer | null = null
  let currentProfile = ''
  let initSegment: Uint8Array | null = null
  let mimeType = 'video/mp4'
  let ignoreData = false

  let slot: MseSlot | null = null
  let nextSlot: MseSlot | null = null

  let pendingGop: Uint8Array[] = []
  let pendingGopTimestamp = 0
  let posterAppended = false

  let seekBuffer: Uint8Array[] = []
  let seekTargetUs = 0
  let seekGapEnd = 0

  let gapEnd = 0
  let fetchInFlight = false
  let rafId: number | null = null
  let tsEpoch = 0
  let tsEpochAtLastUpdate = -1
  const debug = typeof localStorage !== 'undefined' && localStorage.getItem('debug_player') !== null


  function sendFetch(from: number, to: number) {
    fetchInFlight = true
    streamer?.fetch(currentProfile, from, to)
  }

  function sendGoLive() {
    fetchInFlight = true
    streamer?.goLive(currentProfile)
  }

  // ---------------------------------------------------------------------------
  // Slot operations
  // ---------------------------------------------------------------------------

  function createSlot(): MseSlot {
    const vid = document.createElement('video')
    vid.className = 'w-full'
    vid.muted = true
    vid.autoplay = false
    vid.playsInline = true
    vid.style.display = 'none'
    container?.appendChild(vid)

    const s: MseSlot = {
      video: vid,
      mediaSource: null,
      sourceBuffer: null,
      initData: initSegment ? new Uint8Array(initSegment) : null,
      initAppended: false,
      queue: [],
      appending: false,
      anchors: [],
      bufferEndS: 0,
      pendingWallClock: 0,
    }

    s.mediaSource = new MediaSource()
    vid.src = URL.createObjectURL(s.mediaSource)
    s.mediaSource.addEventListener('sourceopen', () => {
      if (!s.mediaSource || s.mediaSource.readyState !== 'open') return
      slotFlush(s)
    })

    return s
  }

  function destroySlot(s: MseSlot) {
    s.video.pause()
    if (s.video.src)
      URL.revokeObjectURL(s.video.src)
    s.video.removeAttribute('src')
    s.video.parentElement?.removeChild(s.video)
  }

  function slotEnqueue(s: MseSlot, data: Uint8Array) {
    if (!s.mediaSource || s.mediaSource.readyState !== 'open') {
      s.queue.push(data)
      return
    }

    if (!s.sourceBuffer && s.initData) {
      try {
        s.sourceBuffer = s.mediaSource.addSourceBuffer(mimeType)
        s.sourceBuffer.mode = 'sequence'
        s.sourceBuffer.addEventListener('updateend', () => slotOnUpdateEnd(s))
      } catch {
        return
      }
    }

    if (!s.initAppended && s.initData) {
      s.queue.unshift(s.initData)
      s.initAppended = true
    }

    s.queue.push(data)
    slotAppendNext(s)
  }

  function slotFlush(s: MseSlot) {
    if (s.queue.length > 0 && s.initData && !s.sourceBuffer)
      slotEnqueue(s, new Uint8Array(0))
    else if (s.queue.length > 0)
      slotAppendNext(s)
  }

  function slotAppendNext(s: MseSlot) {
    if (s.appending || !s.sourceBuffer || s.sourceBuffer.updating || s.queue.length === 0) return
    s.appending = true
    const chunk = s.queue.shift()!
    if (chunk.length === 0) {
      s.appending = false
      return
    }
    const prft = extractPrftWallClock(chunk)
    if (prft > 0)
      s.pendingWallClock = prft
    try {
      s.sourceBuffer.appendBuffer(chunk as unknown as ArrayBuffer)
      if (s === slot && !paused.value && !blocked.value && s.video.paused)
        s.video.play().catch(() => {})
    } catch {
      s.appending = false
      s.queue = []
    }
  }

  function slotOnUpdateEnd(s: MseSlot) {
    s.appending = false
    if (s.video.buffered.length > 0) {
      const prevEndS = s.bufferEndS
      s.bufferEndS = s.video.buffered.end(s.video.buffered.length - 1)
      if (s.pendingWallClock > 0) {
        s.anchors.push({ mediaTimeS: prevEndS, wallClockUs: s.pendingWallClock })
        if (s === slot && seekTargetUs > 0) {
          const offsetS = prevEndS + (seekTargetUs - s.pendingWallClock) / 1_000_000
          if (offsetS > 0 && offsetS < s.bufferEndS)
            s.video.currentTime = offsetS
          seekTargetUs = 0
        }
        s.pendingWallClock = 0
      }
    }
    slotAppendNext(s)
    if (s === slot) {
      updateTimestamp()
      trimBuffer()
    }
  }

  function promoteNextSlot() {
    if (!nextSlot || !slot) return
    if (debug) console.log('SWAP to nextSlot')
    destroySlot(slot)
    slot = nextSlot
    nextSlot = null
    gapEnd = 0
    slot.video.style.display = ''
    slot.video.playbackRate = rate.value
    slot.video.play().catch(() => {})
    tsEpoch++
    slotAborted = false
    state = 'streaming'
  }

  // ---------------------------------------------------------------------------
  // Wallclock
  // ---------------------------------------------------------------------------

  function extractPrftWallClock(data: Uint8Array): number {
    if (data.length < 40) return 0
    const view = new DataView(data.buffer, data.byteOffset, data.byteLength)
    const moofSize = view.getUint32(0)
    if (moofSize < 40 || moofSize > data.length) return 0
    const prftOffset = moofSize - 32
    if (prftOffset + 24 > data.length) return 0
    const type = String.fromCharCode(
      data[prftOffset + 4], data[prftOffset + 5],
      data[prftOffset + 6], data[prftOffset + 7])
    if (type !== 'prft') return 0
    return Number(view.getBigUint64(prftOffset + 16))
  }

  function updateTimestamp() {
    if (!slot || slot.video.readyState < 2 || slot.anchors.length === 0) return
    const ct = slot.video.currentTime
    let anchor = slot.anchors[0]
    for (let i = slot.anchors.length - 1; i >= 0; i--) {
      if (slot.anchors[i].mediaTimeS <= ct) {
        anchor = slot.anchors[i]
        break
      }
    }
    const newTs = anchor.wallClockUs + (ct - anchor.mediaTimeS) * 1_000_000
    const epochChanged = tsEpochAtLastUpdate !== tsEpoch
    if (newTs >= timestampUs.value || epochChanged) {
      timestampUs.value = newTs
      tsEpochAtLastUpdate = tsEpoch
    }
    while (slot.anchors.length > 2 && slot.anchors[1].mediaTimeS <= ct)
      slot.anchors.shift()
  }

  function trimBuffer() {
    if (!slot?.sourceBuffer || slot.sourceBuffer.updating) return
    if (slot.video.currentTime > 30) {
      try { slot.sourceBuffer.remove(0, slot.video.currentTime - 30) } catch {}
    }
  }

  // ---------------------------------------------------------------------------
  // Prefetch & slot swap check
  // ---------------------------------------------------------------------------

  function prefetch() {
    if (fetchInFlight || !streamer || !slot || mode.value === 'live') return
    if (state !== 'streaming') return
    if (tsEpochAtLastUpdate !== tsEpoch) return

    const vid = slot.video
    const bufferedAhead = vid.buffered.length > 0
      ? vid.buffered.end(vid.buffered.length - 1) - vid.currentTime
      : 0
    if (bufferedAhead < 10) {
      const fromUs = timestampUs.value + bufferedAhead * 1_000_000
      sendFetch(fromUs, fromUs + 30_000_000)
    }
  }

  let slotAborted = false

  function checkSlotSwap() {
    if (state !== 'buffering-next' || !nextSlot || !slot) return
    const vid = slot.video
    if (!slotAborted && slot.sourceBuffer && !slot.sourceBuffer.updating
        && slot.queue.length === 0 && !slot.appending) {
      try { slot.sourceBuffer.abort() } catch {}
      slotAborted = true
    }
    if (vid.buffered.length > 0) {
      const end = vid.buffered.end(vid.buffered.length - 1)
      if (end - vid.currentTime > 0.2) return
    }
    if (nextSlot.video.buffered.length === 0) return
    promoteNextSlot()
  }

  // ---------------------------------------------------------------------------
  // rAF loop
  // ---------------------------------------------------------------------------

  function startLoop() {
    stopLoop()
    const tick = () => {
      if (!slot) return
      updateTimestamp()
      if (state === 'streaming' || state === 'buffering-next') {
        prefetch()
        checkSlotSwap()
      }
      rafId = requestAnimationFrame(tick)
    }
    rafId = requestAnimationFrame(tick)
  }

  function stopLoop() {
    if (rafId !== null) {
      cancelAnimationFrame(rafId)
      rafId = null
    }
  }

  // ---------------------------------------------------------------------------
  // State transitions
  // ---------------------------------------------------------------------------

  function enterBlocked() {
    state = 'blocked'
    blocked.value = true
    paused.value = true
    posterAppended = false
    pendingGop = []
    pendingGopTimestamp = 0
    if (!slot) {
      slot = createSlot()
      slot.video.style.display = ''
    }
  }

  function enterSeeking(ts?: number) {
    stopLoop()
    state = 'seeking'
    blocked.value = false
    seekBuffer = []
    seekGapEnd = 0
    seekTargetUs = ts ?? 0
    ignoreData = true
    fetchInFlight = false

    if (nextSlot) { destroySlot(nextSlot); nextSlot = null }
    gapEnd = 0

    const oldSlot = slot
    if (oldSlot) oldSlot.video.pause()

    slot = createSlot()
    tsEpoch++
    slotAborted = false

    if (oldSlot) {
      const old = oldSlot
      slot.video.addEventListener('playing', () => {
        slot!.video.style.display = ''
        destroySlot(old)
      }, { once: true })
    } else {
      slot.video.style.display = ''
    }
  }

  function enterStreaming() {
    state = 'streaming'
    blocked.value = false
    if (slot) {
      slot.video.playbackRate = rate.value
      slot.video.play().catch(() => {})
    }
    startLoop()
  }

  function enterBufferingNext(gapEndUs: number) {
    state = 'buffering-next'
    gapEnd = gapEndUs
    slotAborted = false
    if (nextSlot) destroySlot(nextSlot)
    nextSlot = createSlot()
  }

  // ---------------------------------------------------------------------------
  // Data actions
  // ---------------------------------------------------------------------------

  function commitSeek() {
    if (!slot) return
    if (debug) console.log('COMMITSEEK', 'chunks:', seekBuffer.length)
    for (const chunk of seekBuffer)
      slotEnqueue(slot, chunk)
    seekBuffer = []
    enterStreaming()
  }

  // ---------------------------------------------------------------------------
  // Server input handlers
  // ---------------------------------------------------------------------------

  function handleAck() {
    ignoreData = false
  }

  function handleInit(_profile: string, data: Uint8Array) {
    if (ignoreData) return
    initSegment = new Uint8Array(data)
    if (slot && !slot.initAppended) slot.initData = new Uint8Array(data)
    if (nextSlot && !nextSlot.initAppended) nextSlot.initData = new Uint8Array(data)
    const config = parseInitSegment(data)
    if (config)
      mimeType = `video/mp4; codecs="${buildCodecString(config)}"`
  }

  function handleGop(_profile: string, gopTimestamp: number, data: Uint8Array) {
    if (ignoreData) return
    const chunk = new Uint8Array(data)

    switch (state) {
      case 'blocked':
        if (!posterAppended && slot) {
          slotEnqueue(slot, chunk)
          posterAppended = true
          slot.video.play().then(() => {
            goLive()
          }).catch(() => {})
        }
        if (gopTimestamp !== pendingGopTimestamp) {
          pendingGop = []
          pendingGopTimestamp = gopTimestamp
        }
        pendingGop.push(chunk)
        break

      case 'seeking':
        seekBuffer.push(chunk)
        if (seekBuffer.length === 1)
          commitSeek()
        break

      case 'streaming':
        if (slot) slotEnqueue(slot, chunk)
        break

      case 'buffering-next':
        if (nextSlot) slotEnqueue(nextSlot, chunk)
        break
    }
  }

  function handleGap(_from: number, to: number) {
    if (debug) console.log('GAP', new Date(_from / 1000).toISOString(), '->', new Date(to / 1000).toISOString(), 'state:', state)
    switch (state) {
      case 'blocked':
        break

      case 'seeking':
        seekGapEnd = to
        break

      case 'streaming':
        enterBufferingNext(to)
        break

      case 'buffering-next':
        if (nextSlot) destroySlot(nextSlot)
        nextSlot = createSlot()
        gapEnd = to
        break
    }
  }

  function handleFetchComplete() {
    if (debug) console.log('FETCHCOMPLETE', 'state:', state, 'seekGapEnd:', seekGapEnd, 'gapEnd:', gapEnd)
    fetchInFlight = false
    switch (state) {
      case 'seeking':
        if (seekGapEnd > 0) {
          const from = seekGapEnd
          seekGapEnd = 0
          sendFetch(from, from + 30_000_000)
        }
        break

      case 'streaming':
        break

      case 'buffering-next':
        if (gapEnd > 0) {
          const from = gapEnd
          gapEnd = 0
          sendFetch(from, from + 30_000_000)
        }
        break
    }
  }

  function handleLive() {
    mode.value = 'live'
    minRate.value = 1
    maxRate.value = 1
    rate.value = 1
    direction.value = 1
    if (slot) slot.video.playbackRate = 1
  }

  function handleRecording() {
    mode.value = 'playback'
    minRate.value = 0.25
    maxRate.value = 4
  }

  // ---------------------------------------------------------------------------
  // User input handlers
  // ---------------------------------------------------------------------------

  function seek(ts: number) {
    if (debug) console.log('USER SEEK', new Date(ts / 1000).toISOString(), 'state:', state)
    enterSeeking(ts)
    sendFetch(ts, ts + 30_000_000)
  }

  function goLive() {
    if (debug) console.log('USER GOLIVE', 'state:', state)
    if (state === 'blocked') {
      blocked.value = false
      paused.value = false
      if (pendingGop.length > 0 && slot) {
        for (const chunk of pendingGop)
          slotEnqueue(slot, chunk)
        pendingGop = []
      }
      if (slot) {
        const vid = slot.video
        if (vid.buffered.length > 0)
          vid.currentTime = vid.buffered.end(vid.buffered.length - 1)
        vid.playbackRate = 1
        vid.play().catch(() => {})
      }
      enterSeeking()
      sendGoLive()
      return
    }
    enterSeeking()
    sendGoLive()
  }

  function togglePause() {
    if (state === 'blocked') {
      goLive()
      return
    }
    paused.value = !paused.value
    if (!slot) return
    if (paused.value)
      slot.video.pause()
    else
      slot.video.play().catch(() => {})
  }

  function scrubStart() {
    paused.value = true
    if (slot) slot.video.pause()
  }

  function scrubMove(ts: number) {
    timestampUs.value = ts
  }

  function scrubEnd(ts: number) {
    seek(ts)
    paused.value = false
  }

  function setRate(r: number) {
    if (mode.value === 'live') return
    if (r < 0) r = 0.25
    r = Math.max(minRate.value, Math.min(maxRate.value, r))
    rate.value = r
    direction.value = 1
    if (slot) slot.video.playbackRate = r
  }

  function setProfile(profile: string) {
    currentProfile = profile
    if (mode.value === 'live')
      goLive()
    else
      seek(timestampUs.value)
  }

  // ---------------------------------------------------------------------------
  // Lifecycle
  // ---------------------------------------------------------------------------

  function attach(cont: HTMLElement, s: Streamer, cameraId: string, profile: string) {
    container = cont
    streamer = s
    currentProfile = profile

    s.onAck = handleAck
    s.onInit = handleInit
    s.onGop = handleGop
    s.onGap = handleGap
    s.onFetchComplete = handleFetchComplete
    s.onLive = handleLive
    s.onRecording = handleRecording

    s.connect(cameraId)

    watch(s.status, (status) => {
      if (status === 'connected') {
        enterBlocked()
        sendGoLive()
      }
    })
  }

  function detach() {
    stopLoop()
    if (slot) { destroySlot(slot); slot = null }
    if (nextSlot) { destroySlot(nextSlot); nextSlot = null }
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
    container = null
  }

  function stop() {
    detach()
    state = 'blocked'
    timestampUs.value = 0
    rate.value = 1
    direction.value = 1
    paused.value = false
    buffering.value = false
    blocked.value = false
    posterAppended = false
    pendingGop = []
    pendingGopTimestamp = 0
    seekBuffer = []
    seekTargetUs = 0
    seekGapEnd = 0
    gapEnd = 0
    ignoreData = false
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
