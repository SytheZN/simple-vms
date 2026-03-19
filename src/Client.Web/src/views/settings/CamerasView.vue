<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { CameraListItem, CreateCameraRequest, DiscoveredCamera } from '@/types/api'

const cameras = ref<CameraListItem[]>([])
const error = ref('')
const loading = ref(true)

const addExpanded = ref(false)
const addAddress = ref('')
const addName = ref('')
const addUsername = ref('')
const addPassword = ref('')
const adding = ref(false)

const discovering = ref(false)
const discovered = ref<DiscoveredCamera[]>([])
const discoveryRan = ref(false)
const discoveryDismissed = ref(false)
const discoverySubnets = ref('')
const discoveryUsername = ref('')
const discoveryPassword = ref('')

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

async function loadSettings() {
  try {
    const s = await api.system.settings()
    discoverySubnets.value = s.discoverySubnets?.join(', ') ?? ''
    discoveryUsername.value = s.defaultCredentials?.username ?? ''
    discoveryPassword.value = s.defaultCredentials?.password ?? ''
  } catch {
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

async function runDiscovery() {
  discovering.value = true
  discovered.value = []
  discoveryRan.value = false
  discoveryDismissed.value = false
  error.value = ''
  try {
    const subnets = discoverySubnets.value
      .split(',')
      .map(s => s.trim())
      .filter(Boolean)
    discovered.value = await api.discovery.run({
      subnets: subnets.length > 0 ? subnets : undefined,
      credentials: discoveryUsername.value
        ? { username: discoveryUsername.value, password: discoveryPassword.value }
        : undefined,
    })
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    discovering.value = false
    discoveryRan.value = true
  }
}

async function addDiscovered(cam: DiscoveredCamera) {
  try {
    const body: CreateCameraRequest = { address: cam.address, name: cam.name ?? undefined }
    if (discoveryUsername.value)
      body.credentials = { username: discoveryUsername.value, password: discoveryPassword.value }
    await api.cameras.create(body)
    cam.alreadyAdded = true
    await loadCameras()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

onMounted(async () => {
  await Promise.all([loadCameras(), loadSettings()])
})
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">Cameras</h1>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <div class="card overflow-hidden bg-surface-sunken!">
      <button
        class="w-full flex items-center justify-between p-4 text-sm font-medium text-text"
        @click="addExpanded = !addExpanded"
      >
        Add Cameras
        <i class="ph ph-caret-down icon-sm transition-transform" :class="{ 'rotate-180': addExpanded }"></i>
      </button>
      <div v-if="addExpanded" class="border-t border-border p-4 space-y-4">
        <div class="grid grid-cols-2 gap-6">
          <div class="card p-6 space-y-4">
            <h2 class="section-subheading">Discovery</h2>
            <div class="space-y-3">
              <div class="space-y-1">
                <label class="label">Subnets</label>
                <input class="input" v-model="discoverySubnets" placeholder="192.168.2.0/24, 10.0.1.0/24" />
                <span class="text-xs text-text-muted">Comma-separated CIDR ranges. Leave empty for local network only.</span>
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div class="space-y-1">
                  <label class="label">Username</label>
                  <input class="input" v-model="discoveryUsername" placeholder="admin" />
                </div>
                <div class="space-y-1">
                  <label class="label">Password</label>
                  <input class="input" type="password" v-model="discoveryPassword" />
                </div>
              </div>
            </div>
            <button class="btn btn-secondary" :disabled="discovering" @click="runDiscovery">
              <div v-if="discovering" class="spinner spinner-sm"></div>
              <i v-else class="ph ph-magnifying-glass icon-sm"></i>
              {{ discovering ? 'Scanning...' : 'Discover' }}
            </button>
          </div>
          <div class="card p-6 space-y-4">
            <h2 class="section-subheading">Manual Add</h2>
            <div class="space-y-3">
              <div class="space-y-1">
                <label class="label">Address</label>
                <input class="input" v-model="addAddress" placeholder="http://192.168.1.100/onvif/device_service" />
              </div>
              <div class="space-y-1">
                <label class="label">Name (optional)</label>
                <input class="input" v-model="addName" placeholder="Front Door" />
              </div>
              <div class="space-y-1">
                <label class="label">Username</label>
                <input class="input" v-model="addUsername" placeholder="admin" />
              </div>
              <div class="space-y-1">
                <label class="label">Password</label>
                <input class="input" type="password" v-model="addPassword" />
              </div>
            </div>
            <button class="btn btn-primary" :disabled="adding || !addAddress" @click="addCamera">
              <div v-if="adding" class="spinner spinner-sm"></div>
              <i v-else class="ph ph-plus icon-sm"></i>
              Add
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Discovery results -->
    <section v-if="discoveryRan && !discoveryDismissed" class="space-y-4">
      <div class="flex items-center justify-between">
        <h2 class="section-subheading">Discovered Cameras</h2>
        <button class="btn btn-ghost btn-sm" @click="discoveryDismissed = true">
          <i class="ph ph-x icon-sm"></i> Dismiss
        </button>
      </div>
      <div v-if="discovered.length === 0" class="card p-6 flex flex-col items-center gap-2">
        <i class="ph ph-video-camera-slash icon-xl text-text-muted"></i>
        <p class="text-sm text-text-muted">No cameras found.</p>
      </div>
      <div v-else class="card overflow-hidden">
        <table class="table">
          <thead>
            <tr>
              <th>Address</th>
              <th>Hostname</th>
              <th>Name</th>
              <th>Manufacturer</th>
              <th>Model</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="cam in discovered" :key="cam.address">
              <td class="font-mono">{{ cam.address }}</td>
              <td class="text-text-muted">{{ cam.hostname ?? '--' }}</td>
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
    </section>

    <!-- Camera list -->
    <section class="space-y-4">
      <h2 class="section-subheading">Registered Cameras</h2>

      <div v-if="loading" class="flex justify-center py-12">
        <div class="spinner spinner-lg"></div>
      </div>

      <div v-else-if="cameras.length === 0" class="text-sm text-text-muted">
        No cameras registered.
      </div>

      <div v-else class="card overflow-hidden">
        <table class="table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Address</th>
              <th>Streams</th>
              <th>Capabilities</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="cam in cameras" :key="cam.id">
              <td class="font-medium">{{ cam.name }}</td>
              <td class="font-mono text-text-muted text-xs">{{ cam.address }}</td>
              <td class="text-xs text-text-muted">
                <span v-for="s in cam.streams" :key="s.profile" class="mr-2">
                  {{ s.profile }}: {{ s.resolution }} {{ s.codec }}
                </span>
              </td>
              <td class="text-xs text-text-muted">{{ cam.capabilities?.join(', ') ?? '--' }}</td>
              <td class="text-right">
                <button class="btn btn-ghost btn-sm text-danger" @click="deleteCamera(cam.id)" title="Delete">
                  <i class="ph ph-trash icon-sm"></i>
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
