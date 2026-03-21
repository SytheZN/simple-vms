<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
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

const liveStream = useLiveStream(cameraId, selectedProfile)

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

function startStream() {
  if (videoRef.value) {
    liveStream.start(videoRef.value)
  }
}

watch(selectedProfile, () => {
  startStream()
})

onMounted(async () => {
  await loadCamera()
  startStream()
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
        <div class="relative bg-black">
          <video
            ref="videoRef"
            class="w-full aspect-video"
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
            class="absolute inset-0 flex items-center justify-center bg-surface-sunken"
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
        </div>
      </div>

      <Timeline
        :camera-id="cameraId"
        :profile="selectedProfile"
        @seek="(_ts: number) => {}"
      />

      <div class="flex items-center gap-4">
        <div class="flex items-center gap-2">
          <span class="text-sm text-text-muted">Profile</span>
          <select
            v-model="selectedProfile"
            class="input input-sm w-auto"
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
        <div class="flex items-center gap-2 text-sm text-text-muted">
          <span
            class="badge"
            :class="camera.status === 'online' ? 'badge-success' : camera.status === 'error' ? 'badge-danger' : 'badge-neutral'"
          >
            <i class="ph-fill ph-circle icon-sm"></i> {{ camera.status }}
          </span>
        </div>
      </div>
    </template>
  </div>
</template>
