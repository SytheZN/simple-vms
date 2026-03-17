<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { CameraEvent, CameraListItem } from '@/types/api'

const events = ref<CameraEvent[]>([])
const cameras = ref<CameraListItem[]>([])
const error = ref('')
const loading = ref(true)

const filterCameraId = ref<string | undefined>(undefined)
const filterType = ref<string | undefined>(undefined)
const limit = ref(100)
const offset = ref(0)

function defaultFrom(): number {
  return (Date.now() - 24 * 60 * 60 * 1000) * 1000
}

function defaultTo(): number {
  return Date.now() * 1000
}

const filterFrom = ref(defaultFrom())
const filterTo = ref(defaultTo())

async function loadEvents() {
  loading.value = true
  error.value = ''
  try {
    events.value = await api.events.list({
      cameraId: filterCameraId.value,
      type: filterType.value,
      from: filterFrom.value,
      to: filterTo.value,
      limit: limit.value,
      offset: offset.value,
    })
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

async function loadCameras() {
  try {
    cameras.value = await api.cameras.list()
  } catch {
    // cameras list is for the filter dropdown; non-critical
  }
}

function formatTime(micros: number): string {
  return new Date(micros / 1000).toLocaleString()
}

function eventIcon(type: string): string {
  if (type.includes('motion')) return 'ph ph-person'
  if (type.includes('tamper')) return 'ph ph-shield-warning'
  if (type.includes('disconnect') || type.includes('connection')) return 'ph ph-wifi-slash'
  return 'ph ph-lightning'
}

function eventIconColor(type: string): string {
  if (type.includes('tamper') || type.includes('disconnect') || type.includes('connection')) return 'text-danger'
  if (type.includes('motion')) return 'text-warning'
  return 'text-text-muted'
}

function cameraName(id: string): string {
  return cameras.value.find(c => c.id === id)?.name ?? id
}

function prevPage() {
  offset.value = Math.max(0, offset.value - limit.value)
  loadEvents()
}

function nextPage() {
  offset.value += limit.value
  loadEvents()
}

onMounted(() => {
  loadCameras()
  loadEvents()
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">Events</h1>

    <div class="flex flex-wrap gap-3">
      <select class="input w-auto" v-model="filterCameraId" @change="offset = 0; loadEvents()">
        <option :value="undefined">All Cameras</option>
        <option v-for="cam in cameras" :key="cam.id" :value="cam.id">{{ cam.name }}</option>
      </select>
      <input class="input w-auto" v-model="filterType" placeholder="Event type" @change="offset = 0; loadEvents()" />
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

    <template v-else>
      <div v-if="events.length === 0" class="flex flex-col items-center py-12 gap-3">
        <i class="ph ph-lightning-slash icon-xl text-text-muted"></i>
        <p class="text-text-muted">No events found.</p>
      </div>

      <div v-else class="card overflow-hidden">
        <table class="table">
          <thead>
            <tr>
              <th>Camera</th>
              <th>Event</th>
              <th>Start</th>
              <th>End</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="evt in events" :key="evt.id">
              <td>{{ cameraName(evt.cameraId) }}</td>
              <td>
                <span class="flex items-center gap-2">
                  <i class="icon-sm" :class="[eventIcon(evt.type), eventIconColor(evt.type)]"></i>
                  {{ evt.type }}
                </span>
              </td>
              <td class="font-mono text-text-muted">{{ formatTime(evt.startTime) }}</td>
              <td class="font-mono text-text-muted">{{ evt.endTime ? formatTime(evt.endTime) : '--' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="flex items-center justify-between">
        <button class="btn btn-secondary btn-sm" :disabled="offset === 0" @click="prevPage">
          <i class="ph ph-caret-left icon-sm"></i> Previous
        </button>
        <span class="text-xs text-text-muted">Showing {{ offset + 1 }}-{{ offset + events.length }}</span>
        <button class="btn btn-secondary btn-sm" :disabled="events.length < limit" @click="nextPage">
          Next <i class="ph ph-caret-right icon-sm"></i>
        </button>
      </div>
    </template>
  </div>
</template>
