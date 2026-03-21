<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, ApiError } from '@/api/client'
import { useLiveStream } from '@/composables/useLiveStream'
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
const isFullscreen = ref(false)

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

const liveStream = useLiveStream(cameraId, selectedProfile)

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

watch(() => liveStream.lagMs.value, (lag) => {
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
  if (!videoRef.value || !liveStream.wallClockSync.value) return
  const sync = liveStream.wallClockSync.value
  const elapsed = videoRef.value.currentTime - sync.presentationTimeSec
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

const paused = ref(false)

function startStream() {
  if (videoRef.value) {
    liveStream.start(videoRef.value)
    paused.value = false
  }
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

function jumpToLive() {
  if (!videoRef.value) return
  if (videoRef.value.buffered.length > 0) {
    videoRef.value.currentTime = videoRef.value.buffered.end(videoRef.value.buffered.length - 1)
  }
  if (videoRef.value.paused) {
    videoRef.value.play()
    paused.value = false
  }
}

watch(selectedProfile, () => {
  startStream()
})

onMounted(async () => {
  document.addEventListener('fullscreenchange', onFullscreenChange)
  clockTimer = setInterval(updateVideoTime, 250)
  await loadCamera()
  startStream()
})

onUnmounted(() => {
  document.removeEventListener('fullscreenchange', onFullscreenChange)
  if (clockTimer) clearInterval(clockTimer)
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
            v-if="liveStream.status.value === 'blocked'"
            class="absolute inset-0 flex items-center justify-center bg-black/50 cursor-pointer"
            @click="startStream"
          >
            <i class="ph-fill ph-play-circle icon-xl text-primary"></i>
          </div>
          <div
            v-else-if="liveStream.status.value !== 'streaming'"
            class="absolute inset-0 flex items-center justify-center"
          >
            <div v-if="liveStream.status.value === 'connecting'" class="spinner spinner-lg"></div>
            <div v-else-if="liveStream.status.value === 'error'" class="text-center space-y-2">
              <i class="ph ph-warning icon-xl text-danger"></i>
              <p class="text-sm text-text-muted">{{ liveStream.error.value }}</p>
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
          <button class="btn btn-ghost btn-sm" @click="togglePause">
            <i class="ph icon-sm" :class="paused ? 'ph-play' : 'ph-pause'"></i>
          </button>
          <button class="btn btn-ghost btn-sm" @click="jumpToLive" title="Jump to live">
            <i class="ph ph-skip-forward icon-sm"></i>
          </button>
          <div class="flex-1 text-center">
            <span class="text-xs font-mono text-text-muted">{{ currentTimeDisplay }}</span>
          </div>
          <span
            class="badge"
            :class="liveStream.status.value === 'streaming' ? 'badge-success'
              : camera.status === 'online' ? 'badge-success'
              : camera.status === 'error' ? 'badge-danger'
              : 'badge-neutral'"
          >
            <i class="ph-fill ph-circle icon-sm"></i>
            {{ liveStream.status.value === 'streaming' ? 'Live' : camera.status }}
          </span>
          <span v-if="selectedStream" class="text-xs text-text-muted">{{ selectedStream.resolution }}</span>
          <div class="flex items-center gap-2">
            <span class="section-subheading">Profile</span>
            <select
              v-model="selectedProfile"
              class="input"
              style="width: auto; padding: 4px 8px; font-size: 12px;"
            >
              <option
                v-for="s in camera.streams"
                :key="s.profile"
                :value="s.profile"
              >
                {{ s.profile }} ({{ s.resolution }} {{ s.codec }})
              </option>
            </select>
          </div>
        </div>

        <Timeline
          :camera-id="cameraId"
          :profile="selectedProfile"
          @seek="(_ts: number) => {}"
        />
      </div>
    </template>
  </div>
</template>
