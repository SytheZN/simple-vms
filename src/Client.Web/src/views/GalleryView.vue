<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { api, ApiError } from '@/api/client'
import { useGalleryThumbnails } from '@/composables/useGalleryThumbnails'
import { useServerEvents } from '@/composables/useServerEvents'
import type { CameraListItem, LiveEvent } from '@/types/api'

const FLASH_DURATION_MS = 800

const cameras = ref<CameraListItem[]>([])
const error = ref('')
const loading = ref(true)
const flashing = ref<Record<string, boolean>>({})

const flashTimers = new Map<string, number>()

const {
  urls: thumbnailUrls,
  sync: syncThumbnails,
  stop: stopThumbnails
} = useGalleryThumbnails()

function onServerEvent(event: LiveEvent) {
  if (['config', 'added', 'removed', 'status'].includes(event.type)) {
    loadCameras()
    return
  }

  if (!event.ended) flashCamera(event.cameraId)
}

function flashCamera(cameraId: string) {
  const running = flashTimers.get(cameraId)
  if (running !== undefined) window.clearTimeout(running)

  flashing.value = { ...flashing.value, [cameraId]: true }
  flashTimers.set(cameraId, window.setTimeout(() => {
    const { [cameraId]: _done, ...rest } = flashing.value
    flashing.value = rest
    flashTimers.delete(cameraId)
  }, FLASH_DURATION_MS))
}

const { start: startEvents, stop: stopEvents } = useServerEvents(onServerEvent)

async function loadCameras() {
  loading.value = cameras.value.length === 0
  try {
    cameras.value = await api.cameras.list()
    syncThumbnails(cameras.value)
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

function statusIcon(status: string): string {
  if (status === 'online') return 'ph ph-video-camera'
  if (status === 'error') return 'ph ph-warning'
  return 'ph ph-video-camera-slash'
}

function statusBadge(status: string): string {
  if (status === 'online') return 'badge-success'
  if (status === 'error') return 'badge-danger'
  return 'badge-neutral'
}

function statusIconColor(status: string): string {
  if (status === 'error') return 'text-danger'
  return 'text-text-muted'
}

function qualityStreams(cam: CameraListItem) {
  return cam.streams.filter(s => s.kind === 'quality')
}

onMounted(() => {
  loadCameras()
  startEvents()
})

onUnmounted(() => {
  stopEvents()
  stopThumbnails()
  for (const timer of flashTimers.values()) window.clearTimeout(timer)
  flashTimers.clear()
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">Gallery</h1>

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

    <div v-else-if="cameras.length === 0" class="flex flex-col items-center py-12 gap-3">
      <i class="ph ph-video-camera-slash icon-xl text-text-muted"></i>
      <p class="text-text-muted">No cameras found.</p>
    </div>

    <div v-else class="grid grid-cols-3 gap-4">
      <router-link v-for="cam in cameras" :key="cam.id" :to="`/gallery/${cam.id}`" class="card overflow-hidden cursor-pointer hover:shadow-dropdown transition-shadow" :class="{ 'card-flash': flashing[cam.id] }">
        <div class="aspect-video bg-surface-sunken flex items-center justify-center">
          <img v-if="thumbnailUrls[cam.id]"
               :src="thumbnailUrls[cam.id]"
               :alt="cam.name"
               class="w-full h-full object-contain" />
          <i v-else class="icon-xl" :class="[statusIcon(cam.status), statusIconColor(cam.status)]"></i>
        </div>
        <div class="p-3 space-y-2">
          <div class="flex items-center justify-between">
            <span class="text-sm font-medium text-text">{{ cam.name }}</span>
            <span class="badge" :class="statusBadge(cam.status)">
              <i class="ph-fill ph-circle icon-sm"></i> {{ cam.status }}
            </span>
          </div>
          <div class="flex gap-2 text-xs text-text-muted">
            <span v-for="s in qualityStreams(cam)" :key="s.profile">{{ s.resolution }} {{ s.codec }}</span>
          </div>
        </div>
      </router-link>
    </div>
  </div>
</template>
