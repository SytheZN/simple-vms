<script setup lang="ts">
import { ref, shallowRef, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, ApiError } from '@/api/client'
import { usePlayer, type Player } from '@/composables/usePlayer'
import { usePlayerFallback } from '@/composables/usePlayerFallback'
import { useStreamer } from '@/composables/useStreamer'
import Timeline from '@/components/Timeline.vue'
import type { CameraListItem } from '@/types/api'

async function supportsWebCodecsHevc(): Promise<boolean> {
  if (typeof VideoDecoder === 'undefined') return false
  try {
    const result = await VideoDecoder.isConfigSupported({
      codec: 'hev1.1.6.L93.B0',
      codedWidth: 1920,
      codedHeight: 1080,
    })
    return !!result.supported
  } catch {
    return false
  }
}

function buildRateSteps(min: number, max: number): number[] {
  const all: number[] = []
  for (let v = -5; v <= -3; v++) all.push(v)
  for (let i = -8; i <= -1; i++) all.push(i * 0.25)
  for (let i = 1; i <= 8; i++) all.push(i * 0.25)
  for (let v = 3; v <= 5; v++) all.push(v)
  return all.filter(v => v >= min && v <= max)
}

const route = useRoute()
const cameraId = computed(() => route.params.id as string)
const camera = ref<CameraListItem | null>(null)
const error = ref('')
const loading = ref(true)
const selectedProfile = ref('main')
const motionOverlay = ref(false)

const playerContainerRef = ref<HTMLDivElement | null>(null)
const playerRef = ref<HTMLDivElement | null>(null)
const timelineRef = ref<InstanceType<typeof Timeline> | null>(null)
const isFullscreen = ref(false)

const player = shallowRef<Player | null>(null)
const streamer = useStreamer()

const playerState = ref({
  timestampUs: 0,
  rate: 1,
  direction: 1 as 1 | -1,
  paused: false,
  buffering: false,
  blocked: false,
  mode: 'live' as 'live' | 'playback',
  minRate: 1,
  maxRate: 1,
})

function syncState() {
  if (!player.value) return
  playerState.value = {
    timestampUs: player.value.timestampUs.value,
    rate: player.value.rate.value,
    direction: player.value.direction.value,
    paused: player.value.paused.value,
    buffering: player.value.buffering.value,
    blocked: player.value.blocked.value,
    mode: player.value.mode.value,
    minRate: player.value.minRate.value,
    maxRate: player.value.maxRate.value,
  }
}

const rateSteps = computed(() => buildRateSteps(playerState.value.minRate, playerState.value.maxRate))
const rateDisabled = computed(() => playerState.value.minRate === playerState.value.maxRate)
const rateIndex = ref(0)
const playbackRate = computed(() => rateSteps.value[rateIndex.value] ?? 1)
const rateTicks = computed(() => rateSteps.value
  .map((v, i) => ({ value: v, pct: (i / Math.max(1, rateSteps.value.length - 1)) * 100 }))
  .filter(t => Number.isInteger(t.value)))

watch(rateIndex, (idx) => {
  const rate = rateSteps.value[idx] ?? 1
  player.value?.setRate(rate)
})

watch([() => playerState.value.minRate, () => playerState.value.maxRate], () => {
  const steps = rateSteps.value
  const oneIdx = steps.indexOf(1)
  rateIndex.value = oneIdx >= 0 ? oneIdx : 0
})

function onRateWheel(e: WheelEvent) {
  if (rateDisabled.value) return
  const max = rateSteps.value.length - 1
  if (e.deltaY < 0)
    rateIndex.value = Math.min(rateIndex.value + 1, max)
  else
    rateIndex.value = Math.max(rateIndex.value - 1, 0)
}

function toggleFullscreen() {
  if (!playerRef.value) return
  if (document.fullscreenElement) {
    document.exitFullscreen()
  } else {
    playerRef.value.requestFullscreen()
  }
}

function onFullscreenChange() {
  isFullscreen.value = !!document.fullscreenElement
}

const selectedStream = computed(() =>
  camera.value?.streams.find(s => s.profile === selectedProfile.value)
)

const currentTimeDisplay = computed(() => {
  if (playerState.value.timestampUs <= 0) return '--'
  return new Date(playerState.value.timestampUs / 1000).toLocaleString([], {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit'
  })
})

async function loadCamera() {
  loading.value = true
  try {
    camera.value = await api.cameras.get(cameraId.value)
    const profiles = camera.value.streams.map(s => s.profile)
    if (profiles.length > 0 && !profiles.includes(selectedProfile.value)) {
      selectedProfile.value = profiles[0]
    }
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

async function startStream() {
  if (!playerContainerRef.value) return

  const webcodecs = await supportsWebCodecsHevc()
  player.value = webcodecs ? usePlayer() : usePlayerFallback()

  player.value.attach(playerContainerRef.value, streamer, cameraId.value, selectedProfile.value)

  watch([player.value.timestampUs, player.value.rate, player.value.direction, player.value.paused,
    player.value.buffering, player.value.blocked, player.value.mode, player.value.minRate, player.value.maxRate], syncState)
  syncState()
}

function cycleProfile() {
  if (!camera.value || !player.value) return
  const profiles = camera.value.streams
  const idx = profiles.findIndex(s => s.profile === selectedProfile.value)
  const next = profiles[(idx + 1) % profiles.length].profile
  selectedProfile.value = next
  player.value.setProfile(next)
}

let resetOnNextTimeUpdate = false

watch(() => playerState.value.timestampUs, () => {
  if (resetOnNextTimeUpdate && playerState.value.mode === 'live' && playerState.value.timestampUs > 0) {
    resetOnNextTimeUpdate = false
    timelineRef.value?.resetWindow()
  }
})

function goLiveWithReset() {
  resetOnNextTimeUpdate = true
  player.value?.goLive()
}

onMounted(async () => {
  document.addEventListener('fullscreenchange', onFullscreenChange)
  await loadCamera()
  startStream()
})

onUnmounted(() => {
  document.removeEventListener('fullscreenchange', onFullscreenChange)
  streamer.disconnect()
  player.value?.stop()
})
</script>

<template>
  <div class="space-y-4">
    <h1 v-if="camera" class="section-heading">{{ camera.name }}</h1>

    <div v-if="error || streamer.error.value" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error || streamer.error.value }}</p>
      </div>
    </div>

    <div v-if="loading" class="flex justify-center py-12">
      <div class="spinner spinner-lg"></div>
    </div>

    <template v-else-if="camera">
      <div class="card overflow-hidden">
        <div ref="playerRef" class="bg-surface-sunken aspect-video relative flex items-center justify-center">
          <div ref="playerContainerRef" class="w-full h-full"></div>
          <!-- Motion overlay -->
          <canvas
            v-if="motionOverlay"
            class="absolute inset-0 w-full h-full pointer-events-none"
            style="image-rendering: pixelated;"
          ></canvas>
          <div
            v-if="playerState.blocked"
            class="absolute inset-0 flex items-center justify-center bg-black/50 cursor-pointer"
            @click="player?.togglePause()"
          >
            <i class="ph-fill ph-play-circle icon-xl text-primary"></i>
          </div>
          <div
            v-else-if="playerState.buffering"
            class="absolute inset-0 flex items-center justify-center bg-black/30"
          >
            <div class="spinner spinner-lg"></div>
          </div>
          <div
            v-else-if="streamer.status.value !== 'connected'"
            class="absolute inset-0 flex items-center justify-center"
          >
            <div v-if="streamer.status.value === 'connecting'" class="spinner spinner-lg"></div>
            <div v-else-if="streamer.status.value === 'error'" class="text-center space-y-2">
              <i class="ph ph-warning icon-xl text-danger"></i>
              <p class="text-sm text-text-muted">{{ streamer.error.value }}</p>
              <button class="btn btn-sm btn-primary" @click="startStream">Retry</button>
            </div>
            <div v-else class="text-center space-y-2">
              <i class="ph ph-video-camera icon-xl text-text-muted"></i>
              <p class="text-sm text-text-muted">Stream not active</p>
              <button class="btn btn-sm btn-primary" @click="startStream">Connect</button>
            </div>
          </div>

          <div class="absolute bottom-3 right-3 flex gap-2">
            <button class="btn btn-ghost btn-sm video-overlay-text" @click="toggleFullscreen"><i class="ph icon-sm" :class="isFullscreen ? 'ph-corners-in' : 'ph-corners-out'"></i></button>
          </div>
        </div>

        <div class="flex items-center gap-3 px-4 py-3 border-t border-border">
          <button class="btn btn-ghost btn-sm" @click="player?.togglePause()">
            <i class="ph icon-sm" :class="playerState.paused ? 'ph-play' : 'ph-pause'"></i>
          </button>
          <button class="btn btn-ghost btn-sm" @click="goLiveWithReset" title="Jump to live">
            <i class="ph ph-skip-forward icon-sm"></i>
          </button>
          <div class="flex items-center gap-3" :class="{ 'opacity-40': rateDisabled }">
            <div class="rate-slider-wrap" @wheel.prevent="onRateWheel" @mousedown.middle.prevent="rateIndex = rateSteps.indexOf(1)">
              <input
                type="range"
                :min="0"
                :max="Math.max(0, rateSteps.length - 1)"
                step="1"
                v-model.number="rateIndex"
                class="rate-slider"
                :disabled="rateDisabled"
                @dblclick="rateIndex = rateSteps.indexOf(1)"
              />
              <div class="rate-slider-ticks">
                <span
                  v-for="t in rateTicks"
                  :key="t.value"
                  class="rate-slider-tick"
                  :class="{ 'rate-slider-tick-accent': t.value === 1 }"
                  :style="{ left: t.pct + '%' }"
                ></span>
              </div>
            </div>
            <span class="text-xs text-text-muted w-8">{{ playbackRate }}x</span>
          </div>
          <div class="flex-1 text-center">
            <span class="text-xs font-mono text-text-muted">{{ currentTimeDisplay }}</span>
          </div>
          <span
            class="badge"
            :class="streamer.status.value === 'connected'
              ? playerState.mode === 'playback' ? 'badge-warning' : 'badge-success'
              : camera.status === 'error' ? 'badge-danger'
              : 'badge-neutral'"
          >
            <i class="ph-fill ph-circle icon-sm"></i>
            {{ playerState.mode === 'playback' ? 'Playback' : streamer.status.value === 'connected' ? 'Live' : camera.status }}
          </span>
          <span v-if="selectedStream" class="text-xs text-text-muted">{{ selectedStream.resolution }}</span>
          <button class="btn btn-ghost btn-sm" @click="cycleProfile">
            {{ selectedProfile }} ({{ selectedStream?.resolution }} {{ selectedStream?.codec }})
          </button>
          <button class="btn btn-ghost btn-sm" @click="motionOverlay = !motionOverlay" :title="motionOverlay ? 'Hide motion overlay' : 'Show motion overlay'">
            <i class="ph ph-person-arms-spread icon-sm"></i>
          </button>
        </div>

        <Timeline
          ref="timelineRef"
          :camera-id="cameraId"
          :profile="selectedProfile"
          :current-time-us="playerState.timestampUs"
          @seek="(ts: number) => player?.seek(ts)"
          @scrub-start="player?.scrubStart()"
          @scrub-move="(ts: number) => player?.scrubMove(ts)"
          @scrub-end="(ts: number) => player?.scrubEnd(ts)"
        />
      </div>
    </template>
  </div>
</template>
