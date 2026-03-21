<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { api } from '@/api/client'
import type { TimelineSpan, TimelineEvent } from '@/types/api'

const props = defineProps<{
  cameraId: string
  profile: string
}>()

const emit = defineEmits<{
  seek: [timestamp: number]
}>()

const containerRef = ref<HTMLDivElement | null>(null)
const barRef = ref<HTMLDivElement | null>(null)
const spans = ref<TimelineSpan[]>([])
const events = ref<TimelineEvent[]>([])
const loading = ref(false)
const dragging = ref(false)

const nowUs = ref(Date.now() * 1000)
const windowHours = ref(4)
const endOffset = ref(defaultOffset())
const windowEnd = computed(() => nowUs.value + endOffset.value)
const windowStart = computed(() => windowEnd.value - windowHours.value * 3600 * 1_000_000)

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
    nowUs.value = Date.now() * 1000
    const now = Date.now()
    if (now - lastInteraction > 5000 && now - lastAutoLoad >= 60000) {
      lastAutoLoad = now
      loadTimeline()
    }
  }, 1000)
}

async function loadTimeline() {
  loading.value = true
  try {
    const result = await api.recordings.timeline(
      props.cameraId,
      windowStart.value,
      windowEnd.value,
      props.profile
    )
    spans.value = result.spans
    events.value = result.events
  } catch {
    spans.value = []
    events.value = []
  } finally {
    loading.value = false
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
  const d = new Date(nowUs.value / 1000)
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

function formatTickLabel(date: Date, crossesDate: boolean, isDayTick: boolean): string {
  if (isDayTick)
    return date.toLocaleDateString([], { day: '2-digit', month: '2-digit' })

  const time = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  if (!crossesDate) return time

  if (date.getHours() === 0 && date.getMinutes() === 0)
    return date.toLocaleDateString([], { day: '2-digit', month: '2-digit' })

  return time
}

function generateDayTicks(start: number, end: number, range: number): { ts: number, pct: number, label: string }[] {
  const stepDays = pickDayInterval(range)
  const anchorMs = todayMidnight().getTime()
  const stepMs = stepDays * 86_400_000
  const startMs = start / 1000
  const endMs = end / 1000
  const labels = []

  const firstStep = Math.ceil((startMs - anchorMs) / stepMs)
  const lastStep = Math.floor((endMs - anchorMs) / stepMs)

  for (let i = firstStep; i <= lastStep; i++) {
    const tick = new Date(anchorMs + i * stepMs)
    const tsUs = tick.getTime() * 1000
    const pct = ((tsUs - start) / range) * 100
    if (pct >= 3 && pct <= 97)
      labels.push({ ts: tsUs, pct, label: formatTickLabel(tick, true, true) })
  }

  return labels
}

function generateHourTicks(start: number, end: number, range: number, intervalMinutes: number): { ts: number, pct: number, label: string }[] {
  const intervalMs = intervalMinutes * 60_000
  const startDate = new Date(start / 1000)
  const endDate = new Date(end / 1000)
  const crossesDate = startDate.getDate() !== endDate.getDate()
    || startDate.getMonth() !== endDate.getMonth()
    || startDate.getFullYear() !== endDate.getFullYear()

  const first = ceilToLocalInterval(start, intervalMinutes)
  const labels = []
  for (let ms = first.getTime(); ms <= end / 1000; ms += intervalMs) {
    const tsUs = ms * 1000
    const pct = ((tsUs - start) / range) * 100
    if (pct >= 3 && pct <= 97) {
      const date = new Date(ms)
      labels.push({ ts: tsUs, pct, label: formatTickLabel(date, crossesDate, false) })
    }
  }
  return labels
}

const timeLabels = computed(() => {
  const start = windowStart.value
  const end = windowEnd.value
  const range = end - start

  const hourInterval = pickHourInterval(range)
  if (hourInterval != null)
    return generateHourTicks(start, end, range, hourInterval)

  return generateDayTicks(start, end, range)
})

const playheadPct = computed(() =>
  Math.max(0, Math.min(100, timestampToPercent(nowUs.value)))
)

let dragStartX = 0
let dragStartOffset = 0

function onPointerDown(e: PointerEvent) {
  markInteraction()
  if (e.button === 1) {
    e.preventDefault()
    endOffset.value = defaultOffset()
    scheduleLoad()
    return
  }
  if (!containerRef.value) return
  e.preventDefault()
  dragging.value = true
  dragStartX = e.clientX
  dragStartOffset = endOffset.value
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (!dragging.value || !barRef.value) return
  const rect = barRef.value.getBoundingClientRect()
  const deltaPct = (e.clientX - dragStartX) / rect.width
  const range = windowHours.value * 3600 * 1_000_000
  endOffset.value = dragStartOffset - deltaPct * range
}

function onPointerUp() {
  if (!dragging.value) return
  markInteraction()
  dragging.value = false
  scheduleLoad()
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

  const oldStart = nowUs.value + endOffset.value - oldRange
  const tsAtCursor = oldStart + cursorPct * oldRange
  const newStart = tsAtCursor - cursorPct * newRange
  const newEnd = newStart + newRange

  windowHours.value = newHours
  endOffset.value = newEnd - nowUs.value
}

function onMiddleClick(e: MouseEvent) {
  if (e.button === 1) e.preventDefault()
}

let fetchTimer: ReturnType<typeof setTimeout> | null = null

function scheduleLoad() {
  if (dragging.value) return
  if (fetchTimer) clearTimeout(fetchTimer)
  fetchTimer = setTimeout(loadTimeline, 500)
}

watch([() => props.cameraId, () => props.profile], scheduleLoad)
watch(windowHours, scheduleLoad)

onMounted(() => {
  startTicking()
  loadTimeline()
})

onUnmounted(() => {
  if (tickTimer) clearInterval(tickTimer)
  if (fetchTimer) clearTimeout(fetchTimer)
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
        <div
          v-for="(span, i) in spans"
          :key="'s' + i"
          class="timeline-span timeline-span-recording"
          :style="{
            left: timestampToPercent(span.startTime) + '%',
            width: Math.max(0.5, timestampToPercent(span.endTime) - timestampToPercent(span.startTime)) + '%'
          }"
        ></div>

        <div
          v-for="evt in events"
          :key="evt.id"
          class="timeline-marker timeline-alert"
          :style="{ left: timestampToPercent(evt.startTime) + '%' }"
          :title="evt.type"
        ></div>

        <div
          class="timeline-marker timeline-playhead"
          :style="{ left: playheadPct + '%' }"
        ></div>

        <div v-if="loading" class="absolute inset-0 flex items-center justify-center">
          <div class="spinner spinner-sm"></div>
        </div>
      </div>

      <div class="relative h-4">
        <div
          v-for="label in timeLabels"
          :key="label.ts"
          class="timeline-tick"
          :style="{ left: label.pct + '%' }"
        >
          {{ label.label }}
        </div>
      </div>
    </div>

    <div class="flex items-center gap-4 text-xs text-text-muted">
      <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 timeline-span-recording rounded-sm"></span> Recording</span>
      <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 timeline-span-motion rounded-sm"></span> Motion</span>
      <span class="flex items-center gap-1"><span class="inline-block w-3 h-0.5 timeline-alert"></span> Alert</span>
    </div>
  </div>
</template>
