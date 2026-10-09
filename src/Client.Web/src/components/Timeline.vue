<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { api } from '@/api/client'
import { eventMarkerClass } from '@/lib/events'
import { TimelineCache, type TimeRange } from '@/lib/timelineCache'

const props = defineProps<{
  cameraId: string
  profile: string
  currentTimeUs: number
}>()

const emit = defineEmits<{
  seek: [timestamp: number]
  scrubStart: []
  scrubMove: [timestamp: number]
  scrubEnd: [timestamp: number]
}>()

function resetWindow() {
  endOffset.value = defaultOffset()
}

defineExpose({ resetWindow })

const containerRef = ref<HTMLDivElement | null>(null)
const barRef = ref<HTMLDivElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)
let cache = new TimelineCache()
const dragging = ref(false)

const windowHours = ref(4)
const endOffset = ref(defaultOffset())
const initialAnchor = Date.now() * 1000

const pendingSeekUs = ref(0)
const playheadUs = computed(() => pendingSeekUs.value || props.currentTimeUs)
watch(() => props.currentTimeUs, () => { pendingSeekUs.value = 0 })

let frozenAnchor = 0
const anchorUs = computed(() => frozenAnchor || playheadUs.value || initialAnchor)
const windowRangeUs = computed(() => windowHours.value * 3600 * 1_000_000)
const windowEnd = computed(() => anchorUs.value + endOffset.value)
const windowStart = computed(() => windowEnd.value - windowRangeUs.value)

const windowsPerStrip = 3
const stripStartUs = ref(0)
const stripRangeUs = ref(0)
const stripEndUs = computed(() => stripStartUs.value + stripRangeUs.value)

const stripStyle = computed(() => ({
  width: windowsPerStrip * 100 + '%',
  transform: stripRangeUs.value > 0
    ? `translateX(${-(windowStart.value - stripStartUs.value) / stripRangeUs.value * 100}%)`
    : 'none',
}))

function stripPercent(ts: number): number {
  return ((ts - stripStartUs.value) / stripRangeUs.value) * 100
}

function stripNeedsReanchor(): boolean {
  const reanchorMarginUs = windowRangeUs.value / 2
  return stripRangeUs.value !== windowRangeUs.value * windowsPerStrip
    || windowStart.value < stripStartUs.value + reanchorMarginUs
    || windowEnd.value > stripEndUs.value - reanchorMarginUs
}

function reanchorStrip() {
  stripRangeUs.value = windowRangeUs.value * windowsPerStrip
  stripStartUs.value = windowStart.value - windowRangeUs.value
  drawStrip()
  fetchMissing()
}

function seekKeepingWindow(ts: number) {
  const end = windowEnd.value
  pendingSeekUs.value = ts
  endOffset.value = end - ts
}

watch([windowStart, windowRangeUs], () => {
  if (stripNeedsReanchor()) reanchorStrip()
}, { immediate: true })

let tickTimer: ReturnType<typeof setInterval> | null = null

function defaultOffset(): number {
  return 0.25 * windowHours.value * 3600 * 1_000_000
}

let lastInteraction = 0
let lastAutoLoad = 0

function markInteraction() {
  lastInteraction = Date.now()
}

function startTicking() {
  tickTimer = setInterval(() => {
    const now = Date.now()
    if (now - lastInteraction > 5000 && now - lastAutoLoad >= 60000) {
      lastAutoLoad = now
      reanchorStrip()
    }
  }, 1000)
}

function fetchMissing() {
  const strip = { from: Math.floor(stripStartUs.value), to: Math.floor(stripEndUs.value) }
  for (const range of cache.missing(strip))
    fetchRange(cache, range)
}

async function fetchRange(target: TimelineCache, range: TimeRange) {
  target.begin(range)
  try {
    const result = await api.recordings.timeline(props.cameraId, range.from, range.to, props.profile)
    target.complete(range, result.spans, result.events.filter(e => e.type === 'motion'), Date.now() * 1000)
  } catch {
    target.fail(range)
    return
  }
  if (target === cache) drawStrip()
}

function resetCache() {
  cache = new TimelineCache()
  drawStrip()
  fetchMissing()
}

function resolveBackground(className: string): string {
  const probe = document.createElement('div')
  probe.className = className
  barRef.value!.appendChild(probe)
  const color = getComputedStyle(probe).backgroundColor
  probe.remove()
  return color
}

function drawStrip() {
  const canvas = canvasRef.value
  const bar = barRef.value
  if (!canvas || !bar || stripRangeUs.value <= 0) return

  const dpr = window.devicePixelRatio || 1
  const width = Math.round(bar.clientWidth * windowsPerStrip * dpr)
  const height = Math.round(bar.clientHeight * dpr)
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width
    canvas.height = height
  }

  const ctx = canvas.getContext('2d')!
  ctx.clearRect(0, 0, width, height)
  const toX = (ts: number) => (ts - stripStartUs.value) / stripRangeUs.value * width

  ctx.fillStyle = resolveBackground('timeline-span-recording')
  for (const span of cache.spans) {
    const x0 = toX(span.startTime)
    const x1 = toX(span.endTime)
    if (x1 < 0 || x0 > width) continue
    ctx.fillRect(x0, 0, Math.max(1, x1 - x0), height)
  }

  const markerWidth = 2 * dpr
  const markerColors = new Map<string, string>()
  for (const evt of cache.events.values()) {
    const x = toX(evt.startTime)
    if (x < -markerWidth || x > width + markerWidth) continue
    let color = markerColors.get(evt.type)
    if (!color) {
      color = resolveBackground(eventMarkerClass(evt.type))
      markerColors.set(evt.type, color)
    }
    ctx.fillStyle = color
    ctx.fillRect(x - markerWidth / 2, 0, markerWidth, height)
  }
}

function timestampToPercent(ts: number): number {
  const range = windowEnd.value - windowStart.value
  if (range <= 0) return 0
  return ((ts - windowStart.value) / range) * 100
}

const hourIntervals = [
  1, 2, 5, 10, 15, 30,
  60, 120, 240, 360, 720,
]

const dayIntervals = [1, 2, 3, 7]

function pickHourInterval(rangeUs: number): number | null {
  const maxTicks = 8
  const rangeMinutes = rangeUs / (60 * 1_000_000)
  for (const m of hourIntervals) {
    if (rangeMinutes / m <= maxTicks) return m
  }
  return null
}

function pickDayInterval(rangeUs: number): number {
  const maxTicks = 8
  const rangeDays = rangeUs / (1440 * 60 * 1_000_000)
  for (const d of dayIntervals) {
    if (rangeDays / d <= maxTicks) return d
  }
  return 7
}

function ceilToLocalInterval(tsUs: number, intervalMinutes: number): Date {
  const date = new Date(tsUs / 1000)
  const intervalMs = intervalMinutes * 60_000
  const localMidnight = new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()
  const sinceLocal = date.getTime() - localMidnight
  const snapped = Math.ceil(sinceLocal / intervalMs) * intervalMs
  return new Date(localMidnight + snapped)
}

function todayMidnight(): Date {
  const d = new Date()
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

function formatDateLabel(date: Date): string {
  return date.toLocaleDateString([], { day: '2-digit', month: '2-digit' })
}

function formatHourLabel(date: Date): string {
  if (date.getHours() === 0 && date.getMinutes() === 0)
    return formatDateLabel(date)
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function generateDayTicks(start: number, end: number, stepDays: number): { ts: number, label: string }[] {
  const anchorMs = todayMidnight().getTime()
  const stepMs = stepDays * 86_400_000
  const labels = []

  const firstStep = Math.ceil((start / 1000 - anchorMs) / stepMs)
  const lastStep = Math.floor((end / 1000 - anchorMs) / stepMs)

  for (let i = firstStep; i <= lastStep; i++) {
    const tick = new Date(anchorMs + i * stepMs)
    labels.push({ ts: tick.getTime() * 1000, label: formatDateLabel(tick) })
  }

  return labels
}

function generateHourTicks(start: number, end: number, intervalMinutes: number): { ts: number, label: string }[] {
  const intervalMs = intervalMinutes * 60_000
  const first = ceilToLocalInterval(start, intervalMinutes)
  const labels = []
  for (let ms = first.getTime(); ms <= end / 1000; ms += intervalMs)
    labels.push({ ts: ms * 1000, label: formatHourLabel(new Date(ms)) })
  return labels
}

const timeLabels = computed(() => {
  const start = stripStartUs.value
  const end = stripEndUs.value
  if (end <= start) return []

  const hourInterval = pickHourInterval(windowRangeUs.value)
  if (hourInterval != null)
    return generateHourTicks(start, end, hourInterval)

  return generateDayTicks(start, end, pickDayInterval(windowRangeUs.value))
})

const scrubTimestamp = ref(0)

const playheadPct = computed(() => {
  const ts = scrubActive.value ? scrubTimestamp.value : playheadUs.value
  if (!ts) return -999
  return timestampToPercent(ts)
})

let dragStartX = 0
let dragStartOffset = 0
let dragMoved = false
const scrubActive = ref(false)

function pctToTimestamp(clientX: number): number {
  if (!barRef.value) return 0
  const rect = barRef.value.getBoundingClientRect()
  const pct = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width))
  const range = windowEnd.value - windowStart.value
  return Math.floor(windowStart.value + pct * range)
}

function onPlayheadDown(e: PointerEvent) {
  e.stopPropagation()
  e.preventDefault()
  markInteraction()
  scrubTimestamp.value = playheadUs.value
  frozenAnchor = anchorUs.value
  scrubActive.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
  emit('scrubStart')
}

function onPlayheadMove(e: PointerEvent) {
  if (!scrubActive.value) return
  const ts = pctToTimestamp(e.clientX)
  scrubTimestamp.value = ts
  emit('scrubMove', ts)
}

function onPlayheadUp(e: PointerEvent) {
  if (!scrubActive.value) return
  markInteraction()
  const ts = pctToTimestamp(e.clientX)
  seekKeepingWindow(ts)
  scrubActive.value = false
  scrubTimestamp.value = 0
  frozenAnchor = 0
  emit('scrubEnd', ts)
}

function onPointerDown(e: PointerEvent) {
  markInteraction()
  if (e.button === 1) {
    e.preventDefault()
    endOffset.value = defaultOffset()
    return
  }
  if (!containerRef.value) return
  e.preventDefault()
  dragging.value = true
  dragMoved = false
  dragStartX = e.clientX
  dragStartOffset = endOffset.value
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (!dragging.value || !barRef.value) return
  const dx = Math.abs(e.clientX - dragStartX)
  if (dx > 3) dragMoved = true
  const rect = barRef.value.getBoundingClientRect()
  const deltaPct = (e.clientX - dragStartX) / rect.width
  endOffset.value = dragStartOffset - deltaPct * windowRangeUs.value
}

function onPointerUp(e: PointerEvent) {
  if (!dragging.value) return
  markInteraction()
  dragging.value = false

  if (!dragMoved && barRef.value) {
    const ts = pctToTimestamp(e.clientX)
    seekKeepingWindow(ts)
    emit('seek', ts)
  }
}

function onWheel(e: WheelEvent) {
  e.preventDefault()
  markInteraction()
  if (!barRef.value) return

  const rect = barRef.value.getBoundingClientRect()
  const cursorPct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width))

  const oldHours = windowHours.value
  const newHours = e.deltaY > 0
    ? Math.min(720, oldHours * 1.5)
    : Math.max(5 / 60, oldHours / 1.5)

  const oldRange = oldHours * 3600 * 1_000_000
  const newRange = newHours * 3600 * 1_000_000

  const oldStart = anchorUs.value + endOffset.value - oldRange
  const tsAtCursor = oldStart + cursorPct * oldRange
  const newStart = tsAtCursor - cursorPct * newRange
  const newEnd = newStart + newRange

  windowHours.value = newHours
  endOffset.value = newEnd - anchorUs.value
}

function onMiddleClick(e: MouseEvent) {
  if (e.button === 1) e.preventDefault()
}

watch([() => props.cameraId, () => props.profile], resetCache)

const barResizeObserver = new ResizeObserver(drawStrip)

onMounted(() => {
  barResizeObserver.observe(barRef.value!)
  startTicking()
})

onUnmounted(() => {
  barResizeObserver.disconnect()
  if (tickTimer) clearInterval(tickTimer)
})
</script>

<template>
  <div class="px-4 pt-2 pb-3 border-t border-border space-y-1">
    <div
      ref="containerRef"
      class="relative select-none touch-none"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @wheel="onWheel"
      @auxclick="onMiddleClick"
    >
      <div ref="barRef" class="timeline-bar">
        <canvas ref="canvasRef" class="timeline-strip h-full" :style="stripStyle"></canvas>

        <div
          class="timeline-marker timeline-playhead"
          :style="{ left: playheadPct + '%' }"
          @pointerdown="onPlayheadDown"
          @pointermove="onPlayheadMove"
          @pointerup="onPlayheadUp"
        ></div>

      </div>

      <div class="relative h-4 overflow-x-clip">
        <div class="timeline-strip" :style="stripStyle">
          <div
            v-for="label in timeLabels"
            :key="label.ts"
            class="timeline-tick"
            :style="{ left: stripPercent(label.ts) + '%' }"
          >
            {{ label.label }}
          </div>
        </div>
      </div>
    </div>

    <div class="flex items-center gap-4 text-xs text-text-muted">
      <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 timeline-span-recording rounded-sm"></span> Recording</span>
      <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 timeline-span-motion rounded-sm"></span> Motion</span>
      <span class="flex items-center gap-1"><span class="inline-block w-3 h-0.5 timeline-event-warning"></span> Motion</span>
    </div>
  </div>
</template>
