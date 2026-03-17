<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { CameraListItem, CreateCameraRequest, DiscoveredCamera } from '@/types/api'

const cameras = ref<CameraListItem[]>([])
const statusFilter = ref<string | undefined>(undefined)
const error = ref('')
const loading = ref(true)

const showAddForm = ref(false)
const addAddress = ref('')
const addName = ref('')
const addUsername = ref('')
const addPassword = ref('')
const adding = ref(false)

const discovering = ref(false)
const discovered = ref<DiscoveredCamera[]>([])

async function loadCameras() {
  loading.value = true
  try {
    cameras.value = await api.cameras.list(statusFilter.value)
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

async function addCamera() {
  adding.value = true
  error.value = ''
  try {
    const body: CreateCameraRequest = { address: addAddress.value }
    if (addName.value) body.name = addName.value
    if (addUsername.value) body.credentials = { username: addUsername.value, password: addPassword.value }
    await api.cameras.create(body)
    showAddForm.value = false
    addAddress.value = ''
    addName.value = ''
    addUsername.value = ''
    addPassword.value = ''
    await loadCameras()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    adding.value = false
  }
}

async function deleteCamera(id: string) {
  try {
    await api.cameras.delete(id)
    await loadCameras()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function restartCamera(id: string) {
  try {
    await api.cameras.restart(id)
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function runDiscovery() {
  discovering.value = true
  discovered.value = []
  error.value = ''
  try {
    discovered.value = await api.discovery.run({})
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    discovering.value = false
  }
}

async function addDiscovered(cam: DiscoveredCamera) {
  try {
    await api.cameras.create({ address: cam.address, name: cam.name ?? undefined })
    cam.alreadyAdded = true
    await loadCameras()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
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
    <div class="flex items-center justify-between">
      <h1 class="section-heading">Gallery</h1>
      <div class="flex gap-2">
        <select class="input w-auto" v-model="statusFilter" @change="loadCameras()">
          <option :value="undefined">All</option>
          <option value="online">Online</option>
          <option value="offline">Offline</option>
          <option value="error">Error</option>
        </select>
        <button class="btn btn-secondary" :disabled="discovering" @click="runDiscovery">
          <div v-if="discovering" class="spinner spinner-sm"></div>
          <i v-else class="ph ph-magnifying-glass icon-sm"></i>
          Discover
        </button>
        <button class="btn btn-primary" @click="showAddForm = !showAddForm">
          <i class="ph ph-plus icon-sm"></i> Add Camera
        </button>
      </div>
    </div>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <div v-if="showAddForm" class="card p-6 space-y-4 max-w-md">
      <h2 class="text-lg font-semibold text-text">Add Camera</h2>
      <div class="space-y-3">
        <div class="space-y-1">
          <label class="label">Address</label>
          <input class="input" v-model="addAddress" placeholder="192.168.1.100" />
        </div>
        <div class="space-y-1">
          <label class="label">Name (optional)</label>
          <input class="input" v-model="addName" placeholder="Front Door" />
        </div>
        <div class="space-y-1">
          <label class="label">Username (optional)</label>
          <input class="input" v-model="addUsername" placeholder="admin" />
        </div>
        <div class="space-y-1">
          <label class="label">Password (optional)</label>
          <input class="input" type="password" v-model="addPassword" />
        </div>
      </div>
      <div class="flex gap-2">
        <button class="btn btn-primary" :disabled="adding || !addAddress" @click="addCamera">
          <div v-if="adding" class="spinner spinner-sm"></div>
          <i v-else class="ph ph-plus icon-sm"></i>
          Add
        </button>
        <button class="btn btn-secondary" @click="showAddForm = false">Cancel</button>
      </div>
    </div>

    <div v-if="discovered.length > 0" class="card overflow-hidden">
      <table class="table">
        <thead>
          <tr>
            <th>Address</th>
            <th>Name</th>
            <th>Manufacturer</th>
            <th>Model</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="cam in discovered" :key="cam.address">
            <td class="font-mono">{{ cam.address }}</td>
            <td>{{ cam.name ?? '--' }}</td>
            <td class="text-text-muted">{{ cam.manufacturer ?? '--' }}</td>
            <td class="text-text-muted">{{ cam.model ?? '--' }}</td>
            <td class="text-right">
              <button
                v-if="!cam.alreadyAdded"
                class="btn btn-primary btn-sm"
                @click="addDiscovered(cam)"
              >
                <i class="ph ph-plus icon-sm"></i> Add
              </button>
              <span v-else class="badge badge-neutral">Added</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="loading" class="flex justify-center py-12">
      <div class="spinner spinner-lg"></div>
    </div>

    <div v-else-if="cameras.length === 0" class="flex flex-col items-center py-12 gap-3">
      <i class="ph ph-video-camera-slash icon-xl text-text-muted"></i>
      <p class="text-text-muted">No cameras found.</p>
    </div>

    <div v-else class="grid grid-cols-3 gap-4">
      <div v-for="cam in cameras" :key="cam.id" class="card overflow-hidden">
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
          <div class="flex gap-1">
            <button class="btn btn-ghost btn-sm" @click="restartCamera(cam.id)" title="Restart">
              <i class="ph ph-arrow-clockwise icon-sm"></i>
            </button>
            <button class="btn btn-ghost btn-sm text-danger" @click="deleteCamera(cam.id)" title="Delete">
              <i class="ph ph-trash icon-sm"></i>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
