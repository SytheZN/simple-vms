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

    <div v-else class="grid grid-cols-3 gap-4">
      <div v-for="(store, i) in storage.stores" :key="i" class="card p-4 space-y-2">
        <div class="flex justify-between text-xs text-text-muted">
          <span>Used</span>
          <span>{{ formatBytes(store.usedBytes) }} / {{ formatBytes(store.totalBytes) }}</span>
        </div>
        <div class="progress-track">
          <div
            class="progress-fill"
            :class="store.totalBytes > 0 && store.usedBytes / store.totalBytes > 0.9 ? 'progress-fill-danger' : store.totalBytes > 0 && store.usedBytes / store.totalBytes > 0.75 ? 'progress-fill-warning' : ''"
            :style="{ width: store.totalBytes > 0 ? (store.usedBytes / store.totalBytes * 100) + '%' : '0%' }"
          ></div>
        </div>
        <div class="flex justify-between text-xs text-text-muted">
          <span>Free: {{ formatBytes(store.freeBytes) }}</span>
          <span>Recordings: {{ formatBytes(store.recordingBytes) }}</span>
        </div>
      </div>
    </div>
  </div>
</template>
