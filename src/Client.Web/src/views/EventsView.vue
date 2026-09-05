<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { api, ApiError } from '@/api/client'
import { useServerEvents } from '@/composables/useServerEvents'
import type { CameraEvent, CameraListItem, LiveEvent } from '@/types/api'
import {
  describeEvent,
  eventTextClass,
  eventDetail,
  eventSource,
  eventTypes,
  eventTypesFor,
  extraMetadata,
  hasDetail,
} from '@/lib/events'

const SYSTEM_SOURCE = '00000000-0000-0000-0000-000000000000'

const events = ref<CameraEvent[]>([])
const cameras = ref<CameraListItem[]>([])
const error = ref('')
const loading = ref(true)
const expanded = ref(new Set<string>())

const filterCameraId = ref<string | undefined>(undefined)
const filterType = ref<string | undefined>(undefined)
const filterText = ref('')
const limit = ref(100)
const offset = ref(0)
const exhausted = ref(false)
const scanning = ref(false)

const MAX_SCAN_PAGES = 20
const FILTER_DEBOUNCE_MS = 300
let filterTimer: number | null = null

function defaultFrom(): number {
  return (Date.now() - 24 * 60 * 60 * 1000) * 1000
}

function defaultTo(): number {
  return Date.now() * 1000
}

const filterFrom = ref(defaultFrom())
const filterTo = ref(defaultTo())

async function fetchPage(append: boolean): Promise<number> {
  const page = await api.events.list({
    cameraId: filterCameraId.value,
    type: filterType.value,
    from: filterFrom.value,
    to: filterTo.value,
    limit: limit.value,
    offset: offset.value,
  })

  events.value = append ? [...events.value, ...page] : page
  exhausted.value = page.length < limit.value
  return page.length
}

async function loadEvents() {
  loading.value = true
  error.value = ''
  expanded.value.clear()
  exhausted.value = false
  try {
    await fetchPage(false)
    await scanForMatches()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

/**
 * The text filter runs over loaded rows, so without this a match beyond the first
 * page reads as no result. Pages are pulled until enough match to fill the list.
 */
async function scanForMatches() {
  if (filterText.value.trim() === '') return

  scanning.value = true
  try {
    for (let page = 0; page < MAX_SCAN_PAGES; page++) {
      if (exhausted.value) return
      if (visibleEvents.value.length >= limit.value) return

      offset.value += limit.value
      if (await fetchPage(true) === 0) return
    }
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    scanning.value = false
  }
}

async function loadCameras() {
  try {
    cameras.value = await api.cameras.list()
  } catch {
    // cameras list is for the filter dropdown; non-critical
  }
}

function onServerEvent(event: LiveEvent) {
  if (event.type.startsWith('__')) return

  const existing = events.value.find(evt => evt.id === event.id)
  if (existing) {
    existing.endTime = event.endTime
    return
  }

  if (event.ended) return
  if (offset.value !== 0) return
  if (filterCameraId.value && filterCameraId.value !== event.cameraId) return
  if (filterType.value && filterType.value !== event.type) return

  events.value.unshift({
    id: event.id,
    cameraId: event.cameraId,
    type: event.type,
    startTime: event.startTime,
    endTime: event.endTime,
    metadata: event.metadata,
  })
}

const { start: startEvents, stop: stopEvents } = useServerEvents(onServerEvent)

function formatTime(micros: number): string {
  return new Date(micros / 1000).toLocaleString()
}

function cameraName(id: string): string {
  return cameras.value.find(c => c.id === id)?.name ?? id
}

function sourceName(evt: CameraEvent): string {
  return evt.cameraId === SYSTEM_SOURCE ? 'System' : cameraName(evt.cameraId)
}

const availableTypes = computed(() => {
  if (filterCameraId.value === undefined) return eventTypes
  if (filterCameraId.value === SYSTEM_SOURCE) return eventTypesFor('system')
  return eventTypesFor('camera')
})

function onSourceChange() {
  if (filterType.value && !availableTypes.value.includes(filterType.value))
    filterType.value = undefined
  offset.value = 0
  loadEvents()
}

function searchText(evt: CameraEvent): string {
  return [
    sourceName(evt),
    evt.type,
    describeEvent(evt.type).label,
    eventDetail(evt) ?? '',
    eventSource(evt) ?? '',
    ...Object.entries(evt.metadata ?? {}).map(([key, value]) => `${key} ${value}`),
  ].join(' ').toLowerCase()
}

const visibleEvents = computed(() => {
  const needle = filterText.value.trim().toLowerCase()
  if (needle === '') return events.value
  return events.value.filter(evt => searchText(evt).includes(needle))
})

const filtering = computed(() => filterText.value.trim() !== '')

watch(filterText, () => {
  if (filterTimer !== null) window.clearTimeout(filterTimer)
  filterTimer = window.setTimeout(() => {
    filterTimer = null
    if (!filtering.value && offset.value > 0) {
      offset.value = 0
      loadEvents()
      return
    }
    scanForMatches()
  }, FILTER_DEBOUNCE_MS)
})

function toggle(evt: CameraEvent) {
  if (!hasDetail(evt)) return
  if (expanded.value.has(evt.id)) expanded.value.delete(evt.id)
  else expanded.value.add(evt.id)
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
  startEvents()
})

onUnmounted(() => {
  stopEvents()
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">Events</h1>

    <div class="flex flex-wrap gap-3">
      <div class="w-48">
        <select class="input" v-model="filterCameraId" @change="onSourceChange">
          <option :value="undefined">All Sources</option>
          <option :value="SYSTEM_SOURCE">System</option>
          <option v-for="cam in cameras" :key="cam.id" :value="cam.id">Camera - {{ cam.name }}</option>
        </select>
      </div>
      <div class="w-40">
        <select class="input" v-model="filterType" @change="offset = 0; loadEvents()">
          <option :value="undefined">All types</option>
          <option v-for="type in availableTypes" :key="type" :value="type">
            {{ describeEvent(type).label }}
          </option>
        </select>
      </div>
      <div class="flex-1 min-w-56">
        <input class="input" v-model="filterText" placeholder="Filter" />
      </div>
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
      <div v-if="visibleEvents.length === 0" class="flex flex-col items-center py-12 gap-3">
        <i class="ph ph-lightning-slash icon-xl text-text-muted"></i>
        <p class="text-text-muted">No events found.</p>
      </div>

      <div v-else class="card overflow-hidden">
        <table class="table">
          <thead>
            <tr>
              <th class="w-8"></th>
              <th>Source</th>
              <th>Event</th>
              <th>Detail</th>
              <th>Start</th>
              <th>End</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="evt in visibleEvents" :key="evt.id">
              <tr :class="hasDetail(evt) ? 'cursor-pointer' : ''" @click="toggle(evt)">
                <td class="text-text-muted">
                  <i
                    v-if="hasDetail(evt)"
                    class="ph icon-sm"
                    :class="expanded.has(evt.id) ? 'ph-caret-down' : 'ph-caret-right'"
                  ></i>
                </td>
                <td>
                  <span class="flex items-center gap-2">
                    <i
                      class="ph icon-sm text-text-muted"
                      :class="evt.cameraId === SYSTEM_SOURCE ? 'ph-monitor' : 'ph-video-camera'"
                    ></i>
                    <span>{{ sourceName(evt) }}</span>
                  </span>
                </td>
                <td>
                  <span class="flex items-center gap-2">
                    <i class="icon-sm" :class="[describeEvent(evt.type).icon, eventTextClass(evt.type)]"></i>
                    <span>{{ describeEvent(evt.type).label }}</span>
                  </span>
                </td>
                <td class="text-text-muted">
                  <div>{{ eventDetail(evt) ?? '--' }}</div>
                  <div v-if="eventSource(evt)" class="text-xs">{{ eventSource(evt) }}</div>
                </td>
                <td class="font-mono text-text-muted">{{ formatTime(evt.startTime) }}</td>
                <td class="font-mono text-text-muted">{{ evt.endTime ? formatTime(evt.endTime) : '--' }}</td>
              </tr>
              <tr v-if="expanded.has(evt.id)">
                <td colspan="6" class="bg-surface-sunken">
                  <div class="space-y-2">
                    <div class="text-xs text-text-muted">
                      Type <span class="font-mono">{{ evt.type }}</span>
                      <span class="mx-2">·</span>
                      ID <span class="font-mono">{{ evt.id }}</span>
                    </div>
                    <dl v-if="extraMetadata(evt).length > 0" class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
                      <template v-for="[key, value] in extraMetadata(evt)" :key="key">
                        <dt class="font-mono text-text-muted">{{ key }}</dt>
                        <dd class="font-mono break-all">{{ value }}</dd>
                      </template>
                    </dl>
                    <p v-else class="text-sm text-text-muted">No further metadata.</p>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

      <div v-if="filtering" class="flex items-center justify-center gap-2 text-xs text-text-muted">
        <div v-if="scanning" class="spinner"></div>
        <span>
          {{ visibleEvents.length }} of {{ events.length }} scanned<template v-if="!exhausted && !scanning"> (more available)</template>
        </span>
      </div>

      <div v-else class="flex items-center justify-between">
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
