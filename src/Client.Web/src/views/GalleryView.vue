<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { CameraListItem } from '@/types/api'

const cameras = ref<CameraListItem[]>([])
const error = ref('')
const loading = ref(true)

async function loadCameras() {
  loading.value = true
  try {
    cameras.value = await api.cameras.list()
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

onMounted(loadCameras)
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
      <router-link v-for="cam in cameras" :key="cam.id" :to="`/gallery/${cam.id}`" class="card overflow-hidden cursor-pointer hover:shadow-dropdown transition-shadow">
        <div class="aspect-video bg-surface-sunken flex items-center justify-center">
          <i class="icon-xl" :class="[statusIcon(cam.status), statusIconColor(cam.status)]"></i>
        </div>
        <div class="p-3 space-y-2">
          <div class="flex items-center justify-between">
            <span class="text-sm font-medium text-text">{{ cam.name }}</span>
            <span class="badge" :class="statusBadge(cam.status)">
              <i class="ph-fill ph-circle icon-sm"></i> {{ cam.status }}
            </span>
          </div>
          <div class="flex gap-2 text-xs text-text-muted">
            <span v-for="s in cam.streams" :key="s.profile">{{ s.resolution }} {{ s.codec }}</span>
          </div>
        </div>
      </router-link>
    </div>
  </div>
</template>
