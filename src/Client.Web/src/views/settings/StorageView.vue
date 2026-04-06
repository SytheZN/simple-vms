<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { StorageResponse } from '@/types/api'

const error = ref('')
const storage = ref<StorageResponse | null>(null)

async function load() {
  try {
    storage.value = await api.system.storage()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

function formatBytes(bytes: number): string {
  if (bytes < 0) return 'Unknown'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`
}

function formatDuration(micros: number): string {
  const seconds = Math.floor(micros / 1_000_000)
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${minutes}m`
}

function totalBytesPerHour(store: StorageResponse['stores'][0]): number {
  if (!store.breakdown?.length) return 0
  return store.breakdown.reduce((sum, item) => {
    if (item.durationMicros <= 0) return sum
    return sum + item.sizeBytes / (item.durationMicros / 1_000_000 / 3600)
  }, 0)
}

function estimateRemaining(store: StorageResponse['stores'][0]): string {
  const rate = totalBytesPerHour(store)
  if (rate <= 0 || store.freeBytes <= 0) return '--'
  const hours = store.freeBytes / rate
  return formatDuration(hours * 3_600_000_000)
}

onMounted(load)
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">Storage</h1>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <div v-if="!storage" class="flex justify-center py-12">
      <div class="spinner spinner-lg"></div>
    </div>

    <div v-else-if="storage.stores.length === 0" class="text-sm text-text-muted">
      No storage providers configured.
    </div>

    <div v-else class="space-y-4">
      <div v-for="(store, i) in storage.stores" :key="i" class="card p-4 space-y-3">
        <div class="flex justify-between text-xs text-text-muted">
          <span>Used</span>
          <span>{{ formatBytes(store.usedBytes) }} / {{ formatBytes(store.totalBytes) }}</span>
        </div>
        <div class="progress-track relative">
          <div
            class="progress-fill absolute inset-y-0 left-0"
            :class="store.totalBytes > 0 && store.usedBytes / store.totalBytes > 0.9 ? 'progress-fill-danger' : store.totalBytes > 0 && store.usedBytes / store.totalBytes > 0.75 ? 'progress-fill-warning' : ''"
            :style="{ width: store.totalBytes > 0 ? (store.usedBytes / store.totalBytes * 100) + '%' : '0%' }"
          ></div>
          <div
            v-if="store.recordingBytes > 0 && store.totalBytes > 0"
            class="progress-fill progress-fill-warning absolute inset-y-0 min-w-1"
            :style="{
              right: (store.freeBytes / store.totalBytes * 100) + '%',
              width: (store.recordingBytes / store.totalBytes * 100) + '%'
            }"
          ></div>
        </div>
        <div class="flex justify-between text-xs text-text-muted">
          <span>Free: {{ formatBytes(store.freeBytes) }}</span>
          <div class="flex items-center gap-3">
            <span class="flex items-center gap-1">
              <span class="inline-block w-2 h-2 rounded-full" style="background: var(--color-primary)"></span>
              Other: {{ formatBytes(store.usedBytes - store.recordingBytes) }}
            </span>
            <span class="flex items-center gap-1">
              <span class="inline-block w-2 h-2 rounded-full" style="background: var(--color-warning)"></span>
              Recordings: {{ formatBytes(store.recordingBytes) }}
            </span>
          </div>
        </div>
        <div v-if="store.breakdown?.length" class="space-y-1 pt-1">
          <h3 class="section-subheading">Breakdown</h3>
          <div class="text-xs space-y-1">
            <div class="flex items-center text-text-muted font-medium">
              <span class="flex-1">Stream</span>
              <span class="w-20 text-right">Duration</span>
              <span class="w-20 text-right">Size</span>
              <span class="w-14 text-right">%</span>
              <span class="w-16 text-right">MB/h</span>
            </div>
            <div v-for="item in store.breakdown" :key="`${item.cameraId}-${item.streamProfile}`"
              class="flex items-center text-text-muted">
              <span class="flex-1">{{ item.cameraName }} <span class="opacity-60">/ {{ item.streamProfile }}</span></span>
              <span class="w-20 text-right">{{ formatDuration(item.durationMicros) }}</span>
              <span class="w-20 text-right">{{ formatBytes(item.sizeBytes) }}</span>
              <span class="w-14 text-right">{{ store.recordingBytes > 0 ? (item.sizeBytes / store.recordingBytes * 100).toFixed(1) + '%' : '--' }}</span>
              <span class="w-16 text-right">{{ item.durationMicros > 0 ? (item.sizeBytes / 1024 / 1024 / (item.durationMicros / 1_000_000 / 3600)).toFixed(1) : '--' }}</span>
            </div>
            <div v-if="store.recordingBytes - store.breakdown.reduce((sum, b) => sum + b.sizeBytes, 0) > 0"
              class="flex items-center text-text-muted">
              <span class="flex-1">Unaccounted <span class="opacity-60">(in progress)</span></span>
              <span class="w-20"></span>
              <span class="w-20 text-right">{{ formatBytes(store.recordingBytes - store.breakdown.reduce((sum, b) => sum + b.sizeBytes, 0)) }}</span>
              <span class="w-14 text-right">{{ ((store.recordingBytes - store.breakdown.reduce((sum, b) => sum + b.sizeBytes, 0)) / store.recordingBytes * 100).toFixed(1) + '%' }}</span>
              <span class="w-16"></span>
            </div>
            <div v-if="totalBytesPerHour(store) > 0" class="flex items-center text-text-muted font-medium pt-1 border-t border-border">
              <span class="flex-1">Est. Max Duration</span>
              <span class="w-20 text-right">{{ estimateRemaining(store) }}</span>
              <span class="w-20"></span>
              <span class="w-14"></span>
              <span class="w-16 text-right">{{ (totalBytesPerHour(store) / 1024 / 1024).toFixed(1) }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
