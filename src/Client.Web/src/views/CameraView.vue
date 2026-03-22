<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, ApiError } from '@/api/client'
import { useStream } from '@/composables/useStream'
import Timeline from '@/components/Timeline.vue'
import type { CameraListItem } from '@/types/api'

const route = useRoute()
const cameraId = computed(() => route.params.id as string)
const camera = ref<CameraListItem | null>(null)
const error = ref('')
const loading = ref(true)
const selectedProfile = ref('main')

const videoRef = ref<HTMLVideoElement | null>(null)
const playerRef = ref<HTMLDivElement | null>(null)
const timelineRef = ref<InstanceType<typeof Timeline> | null>(null)
const isFullscreen = ref(false)
const paused = ref(false)
const playbackRate = ref(1)

function rateToSlider(rate: number): number {
  if (rate <= 1) return ((rate - 0.25) / 0.75) * 50
  return 50 + ((rate - 1) / 4) * 50
}

function sliderToRate(val: number): number {
  if (val <= 50) return 0.25 + (val / 50) * 0.75
  return 1 + ((val - 50) / 50) * 4
}

function onRateChange(e: Event) {
  const val = parseFloat((e.target as HTMLInputElement).value)
  const rate = Math.round(sliderToRate(val) * 4) / 4
  playbackRate.value = rate
  if (videoRef.value) videoRef.value.playbackRate = rate
}

const stream = useStream(cameraId, selectedProfile)

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

const sortedProfiles = computed(() => {
  if (!camera.value) return []
  return [...camera.value.streams].sort((a, b) => {
    const resA = parseInt(a.resolution) || 0
    const resB = parseInt(b.resolution) || 0
    return resB - resA
  })
})

let lagOverCount = 0
const lagThresholdMs = 3000
const lagCheckCount = 3

watch(() => stream.lagMs.value, (lag) => {
  if (stream.mode.value !== 'live') return
  if (lag > lagThresholdMs) {
    lagOverCount++
    if (lagOverCount >= lagCheckCount) {
      lagOverCount = 0
      const profiles = sortedProfiles.value
      const currentIdx = profiles.findIndex(s => s.profile === selectedProfile.value)
      if (currentIdx >= 0 && currentIdx < profiles.length - 1) {
        selectedProfile.value = profiles[currentIdx + 1].profile
      }
    }
  } else {
    lagOverCount = 0
  }
})

const videoTimeUs = ref(0)
let clockTimer: ReturnType<typeof setInterval> | null = null

function updateVideoTime() {
  if (!videoRef.value || !stream.wallClockSync.value) return
  const sync = stream.wallClockSync.value
  const elapsed = videoRef.value.currentTime - sync.bufferedEndAtSync
  videoTimeUs.value = sync.wallClockUs + elapsed * 1_000_000
}

const currentTimeDisplay = computed(() => {
  if (videoTimeUs.value <= 0) return '--'
  return new Date(videoTimeUs.value / 1000).toLocaleString([], {
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

async function startLive() {
  if (!videoRef.value) return
  await stream.connect(videoRef.value)
  paused.value = false
}

function cycleProfile() {
  if (!camera.value) return
  const profiles = camera.value.streams
  const idx = profiles.findIndex(s => s.profile === selectedProfile.value)
  selectedProfile.value = profiles[(idx + 1) % profiles.length].profile
}

function togglePause() {
  if (!videoRef.value) return
  if (videoRef.value.paused) {
    videoRef.value.play()
    paused.value = false
  } else {
    videoRef.value.pause()
    paused.value = true
  }
}

function goLive() {
  playbackRate.value = 1
  if (videoRef.value) videoRef.value.playbackRate = 1
  if (stream.mode.value === 'playback') {
    startLive()
    return
  }
  if (!videoRef.value) return
  if (videoRef.value.buffered.length > 0) {
    videoRef.value.currentTime = videoRef.value.buffered.end(videoRef.value.buffered.length - 1)
  }
  if (videoRef.value.paused) {
    videoRef.value.play()
    paused.value = false
  }
}

async function onTimelineSeek(ts: number) {
  if (!videoRef.value) return

  try {
    const meta = await api.playback.metadata(cameraId.value, selectedProfile.value, ts)
    if (meta.from === 0) {
      goLive()
      return
    }

    await stream.connect(videoRef.value, meta.from, meta.segmentId)
    paused.value = false
  } catch {
    goLive()
  }
}

let resetOnNextTimeUpdate = false

watch(videoTimeUs, () => {
  if (resetOnNextTimeUpdate && stream.mode.value === 'live' && videoTimeUs.value > 0) {
    resetOnNextTimeUpdate = false
    timelineRef.value?.resetWindow()
  }
})

function goLiveWithReset() {
  resetOnNextTimeUpdate = true
  goLive()
}

watch(selectedProfile, () => {
  startLive()
})

onMounted(async () => {
  document.addEventListener('fullscreenchange', onFullscreenChange)
  clockTimer = setInterval(updateVideoTime, 250)
  await loadCamera()
  startLive()
})

onUnmounted(() => {
  document.removeEventListener('fullscreenchange', onFullscreenChange)
  if (clockTimer) clearInterval(clockTimer)
  stream.stop()
})
</script>

<template>
  <div class="space-y-4">
    <div class="flex items-center gap-3">
      <router-link to="/" class="btn btn-ghost btn-sm">
        <i class="ph ph-arrow-left icon-sm"></i>
      </router-link>
      <h1 v-if="camera" class="section-heading mb-0">{{ camera.name }}</h1>
    </div>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <div v-if="loading" class="flex justify-center py-12">
      <div class="spinner spinner-lg"></div>
    </div>

    <template v-else-if="camera">
      <div class="card overflow-hidden">
        <div ref="playerRef" class="bg-surface-sunken aspect-video relative flex items-center justify-center">
          <video
            ref="videoRef"
            class="w-full h-full object-contain"
            autoplay
            muted
            playsinline
          ></video>
          <div
            v-if="stream.status.value === 'blocked'"
            class="absolute inset-0 flex items-center justify-center bg-black/50 cursor-pointer"
            @click="startLive"
          >
            <i class="ph-fill ph-play-circle icon-xl text-primary"></i>
          </div>
          <div
            v-else-if="stream.status.value !== 'streaming' && stream.status.value !== 'ended'"
            class="absolute inset-0 flex items-center justify-center"
          >
            <div v-if="stream.status.value === 'connecting'" class="spinner spinner-lg"></div>
            <div v-else-if="stream.status.value === 'error'" class="text-center space-y-2">
              <i class="ph ph-warning icon-xl text-danger"></i>
              <p class="text-sm text-text-muted">{{ stream.error.value }}</p>
              <button class="btn btn-sm btn-primary" @click="startLive">Retry</button>
            </div>
            <div v-else class="text-center space-y-2">
              <i class="ph ph-video-camera icon-xl text-text-muted"></i>
              <p class="text-sm text-text-muted">Stream not active</p>
              <button class="btn btn-sm btn-primary" @click="startLive">Connect</button>
            </div>
          </div>

          <div class="absolute bottom-3 right-3 flex gap-2">
            <button class="btn btn-ghost btn-sm video-overlay-text" @click="toggleFullscreen"><i class="ph icon-sm" :class="isFullscreen ? 'ph-corners-in' : 'ph-corners-out'"></i></button>
          </div>
        </div>

        <div class="flex items-center gap-3 px-4 py-3 border-t border-border">
          <button class="btn btn-ghost btn-sm" @click="togglePause">
            <i class="ph icon-sm" :class="paused ? 'ph-play' : 'ph-pause'"></i>
          </button>
          <button class="btn btn-ghost btn-sm" @click="goLiveWithReset" title="Jump to live">
            <i class="ph ph-skip-forward icon-sm"></i>
          </button>
          <div class="flex items-center gap-1" :class="{ 'opacity-40': stream.mode.value === 'live' }">
            <input
              type="range"
              min="0" max="100" :value="rateToSlider(playbackRate)"
              class="w-32 h-1 accent-primary"
              :disabled="stream.mode.value === 'live'"
              @input="onRateChange"
              @dblclick="playbackRate = 1; if (videoRef) videoRef.playbackRate = 1"
              title="Playback speed"
            />
            <span class="text-xs text-text-muted w-8">{{ playbackRate }}x</span>
          </div>
          <div class="flex-1 text-center">
            <span class="text-xs font-mono text-text-muted">{{ currentTimeDisplay }}</span>
          </div>
          <span
            class="badge"
            :class="stream.status.value === 'streaming' || stream.status.value === 'ended'
              ? stream.mode.value === 'playback' ? 'badge-warning' : 'badge-success'
              : camera.status === 'error' ? 'badge-danger'
              : 'badge-neutral'"
          >
            <i class="ph-fill ph-circle icon-sm"></i>
            {{ stream.mode.value === 'playback' ? 'Playback' : stream.status.value === 'streaming' ? 'Live' : camera.status }}
          </span>
          <span v-if="selectedStream" class="text-xs text-text-muted">{{ selectedStream.resolution }}</span>
          <span v-if="selectedStream" class="text-xs text-text-muted">{{ selectedStream.resolution }}</span>
          <button class="btn btn-ghost btn-sm" @click="cycleProfile">
            {{ selectedProfile }} ({{ selectedStream?.resolution }} {{ selectedStream?.codec }})
          </button>
        </div>

        <Timeline
          ref="timelineRef"
          :camera-id="cameraId"
          :profile="selectedProfile"
          :current-time-us="videoTimeUs"
          @seek="onTimelineSeek"
        />
      </div>
    </template>
  </div>
</template>
