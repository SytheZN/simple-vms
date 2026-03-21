<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
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
const spans = ref<TimelineSpan[]>([])
const events = ref<TimelineEvent[]>([])
const loading = ref(false)
const dragging = ref(false)

const now = Date.now() * 1000
const windowHours = ref(4)
const windowEnd = ref(now)
const windowStart = computed(() => windowEnd.value - windowHours.value * 3600 * 1_000_000)

const playheadPosition = ref(now)

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

function percentToTimestamp(pct: number): number {
  const range = windowEnd.value - windowStart.value
  return windowStart.value + (pct / 100) * range
}

function formatTime(ts: number): string {
  const date = new Date(ts / 1000)
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

const timeLabels = computed(() => {
  const count = 6
  const step = (windowEnd.value - windowStart.value) / count
  const labels = []
  for (let i = 0; i <= count; i++) {
    const ts = windowStart.value + step * i
    labels.push({ ts, pct: (i / count) * 100, label: formatTime(ts) })
  }
  return labels
})

const playheadPct = computed(() =>
  Math.max(0, Math.min(100, timestampToPercent(playheadPosition.value)))
)

function onPointerDown(e: PointerEvent) {
  if (!containerRef.value) return
  dragging.value = true
  ;(e.target as HTMLElement).setPointerCapture(e.pointerId)
  updatePlayhead(e)
}

function onPointerMove(e: PointerEvent) {
  if (!dragging.value) return
  updatePlayhead(e)
}

function onPointerUp() {
  if (!dragging.value) return
  dragging.value = false
  emit('seek', playheadPosition.value)
}

function updatePlayhead(e: PointerEvent) {
  if (!containerRef.value) return
  const rect = containerRef.value.getBoundingClientRect()
  const pct = Math.max(0, Math.min(100, ((e.clientX - rect.left) / rect.width) * 100))
  playheadPosition.value = percentToTimestamp(pct)
}

function onWheel(e: WheelEvent) {
  e.preventDefault()
  if (e.deltaY > 0) {
    windowHours.value = Math.min(48, windowHours.value * 1.5)
  } else {
    windowHours.value = Math.max(0.5, windowHours.value / 1.5)
  }
}

watch([() => props.cameraId, () => props.profile, windowStart], loadTimeline)
onMounted(loadTimeline)
</script>

<template>
  <div class="card p-3 space-y-2">
    <div class="flex items-center justify-between text-xs text-text-muted">
      <span>{{ formatTime(windowStart) }}</span>
      <span class="font-medium text-text">Timeline</span>
      <span>{{ formatTime(windowEnd) }}</span>
    </div>

    <div
      ref="containerRef"
      class="relative h-8 bg-surface-sunken rounded-md cursor-crosshair select-none"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @wheel="onWheel"
    >
      <div
        v-for="(span, i) in spans"
        :key="'s' + i"
        class="absolute top-0 h-full bg-primary-muted rounded-sm"
        :style="{
          left: timestampToPercent(span.startTime) + '%',
          width: Math.max(0.5, timestampToPercent(span.endTime) - timestampToPercent(span.startTime)) + '%'
        }"
      ></div>

      <div
        v-for="evt in events"
        :key="evt.id"
        class="absolute top-0 h-full w-0.5 bg-warning"
        :style="{ left: timestampToPercent(evt.startTime) + '%' }"
        :title="evt.type"
      ></div>

      <div
        class="absolute top-0 h-full w-0.5 bg-primary"
        :style="{ left: playheadPct + '%' }"
      >
        <div class="absolute -top-1 -translate-x-1/2 w-2 h-2 rounded-full bg-primary"></div>
      </div>

      <div v-if="loading" class="absolute inset-0 flex items-center justify-center">
        <div class="spinner spinner-sm"></div>
      </div>
    </div>

    <div class="flex justify-between text-xs text-text-muted">
      <div v-for="label in timeLabels" :key="label.ts" :style="{ position: 'absolute', left: label.pct + '%', transform: 'translateX(-50%)' }">
        {{ label.label }}
      </div>
    </div>
  </div>
</template>
