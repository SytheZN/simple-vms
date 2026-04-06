<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type {
  CameraListItem,
  CreateCameraRequest,
  DiscoveredCamera,
  ProbeResponse,
} from '@/types/api'

const router = useRouter()
const cameras = ref<CameraListItem[]>([])
const error = ref('')
const loading = ref(true)

const discoveryExpanded = ref(false)
const discovering = ref(false)
const discovered = ref<DiscoveredCamera[]>([])
const discoveryRan = ref(false)
const discoveryDismissed = ref(false)
const discoverySubnets = ref('')
const discoveryPorts = ref('')

const dialogOpen = ref(false)
const dialogAddress = ref('')
const dialogName = ref('')
const dialogUsername = ref('')
const dialogPassword = ref('')
const dialogRtspPort = ref('')
const dialogProbe = ref<ProbeResponse | null>(null)
const dialogProbing = ref(false)
const dialogProbeError = ref('')
const dialogAdding = ref(false)

const onvifUsername = ref('')
const onvifPassword = ref('')

async function loadOnvifCreds() {
  try {
    const values = await api.plugins.configValues('onvif') as Record<string, string>
    onvifUsername.value = values.username ?? ''
    onvifPassword.value = values.password ?? ''
  } catch {}
}

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
  } catch {
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
    const ports = discoveryPorts.value
      .split(',')
      .map(s => parseInt(s.trim(), 10))
      .filter(n => !isNaN(n) && n > 0 && n <= 65535)
    discovered.value = await api.discovery.run({
      subnets: subnets.length > 0 ? subnets : undefined,
      ports: ports.length > 0 ? ports : undefined,
    })
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    discovering.value = false
    discoveryRan.value = true
  }
}

function openDialog(cam?: DiscoveredCamera) {
  dialogAddress.value = cam?.address ?? ''
  dialogName.value = cam?.name ?? ''
  dialogUsername.value = cam?.name ? onvifUsername.value : ''
  dialogPassword.value = cam?.name ? onvifPassword.value : ''
  dialogRtspPort.value = ''
  dialogProbe.value = null
  dialogProbeError.value = ''
  dialogOpen.value = true
}

function closeDialog() {
  dialogOpen.value = false
}

async function probeCamera() {
  dialogProbing.value = true
  dialogProbeError.value = ''
  dialogProbe.value = null
  try {
    dialogProbe.value = await api.cameras.probe({
      address: dialogAddress.value,
      credentials: dialogUsername.value
        ? { username: dialogUsername.value, password: dialogPassword.value }
        : undefined,
    })
    if (!dialogName.value && dialogProbe.value.name)
      dialogName.value = dialogProbe.value.name
  } catch (e) {
    if (e instanceof ApiError) dialogProbeError.value = e.message
    else dialogProbeError.value = 'Failed to connect to camera'
  } finally {
    dialogProbing.value = false
  }
}

async function addCamera() {
  dialogAdding.value = true
  error.value = ''
  try {
    const body: CreateCameraRequest = { address: dialogAddress.value }
    if (dialogName.value) body.name = dialogName.value
    if (dialogUsername.value)
      body.credentials = { username: dialogUsername.value, password: dialogPassword.value }
    const port = parseInt(dialogRtspPort.value, 10)
    if (!isNaN(port) && port > 0) body.rtspPortOverride = port
    await api.cameras.create(body)
    closeDialog()
    await loadCameras()
  } catch (e) {
    if (e instanceof ApiError) dialogProbeError.value = e.message
  } finally {
    dialogAdding.value = false
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

onMounted(async () => {
  await Promise.all([loadCameras(), loadSettings(), loadOnvifCreds()])
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

    <!-- Discovery -->
    <div class="card overflow-hidden bg-surface-sunken!">
      <button
        class="w-full flex items-center justify-between p-4 text-sm font-medium text-text"
        @click="discoveryExpanded = !discoveryExpanded"
      >
        Discovery
        <i class="ph ph-caret-down icon-sm transition-transform" :class="{ 'rotate-180': discoveryExpanded }"></i>
      </button>
      <div v-if="discoveryExpanded" class="border-t border-border p-4 space-y-4">
        <div class="grid grid-cols-2 gap-4">
          <div class="space-y-1">
            <label class="label">Subnets</label>
            <input class="input" v-model="discoverySubnets" placeholder="192.168.2.0/24, 10.0.1.0/24" />
            <span class="text-xs text-text-muted">Comma-separated CIDR ranges. Leave empty for local network only.</span>
          </div>
          <div class="space-y-1">
            <label class="label">Ports</label>
            <input class="input" v-model="discoveryPorts" placeholder="80, 8080, 8899" />
            <span class="text-xs text-text-muted">Additional ONVIF ports to scan. Defaults: 80, 8080, 8899.</span>
          </div>
        </div>
        <button class="btn btn-secondary" :disabled="discovering" @click="runDiscovery">
          <div v-if="discovering" class="spinner spinner-sm"></div>
          <i v-else class="ph ph-magnifying-glass icon-sm"></i>
          {{ discovering ? 'Scanning...' : 'Discover' }}
        </button>
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
                  @click="openDialog(cam)"
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
      <div class="flex items-center justify-between">
        <h2 class="section-subheading">Registered Cameras</h2>
        <button class="btn btn-primary btn-sm" @click="openDialog()">
          <i class="ph ph-plus icon-sm"></i> Add Camera
        </button>
      </div>

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
              <td class="text-right whitespace-nowrap">
                <button class="btn btn-ghost btn-sm" @click="router.push(`/settings/cameras/${cam.id}`)" title="Edit">
                  <i class="ph ph-gear icon-sm"></i>
                </button>
                <button class="btn btn-ghost btn-sm text-danger" @click="deleteCamera(cam.id)" title="Delete">
                  <i class="ph ph-trash icon-sm"></i>
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- Add Camera Dialog -->
    <div v-if="dialogOpen" class="modal-container" @click.self="closeDialog">
      <div class="modal-backdrop"></div>
      <div class="relative card p-6 max-w-lg w-full shadow-modal space-y-4">
        <div class="flex items-center justify-between">
          <h2 class="section-subheading">Add Camera</h2>
          <button class="btn btn-ghost btn-sm" @click="closeDialog">
            <i class="ph ph-x icon-sm"></i>
          </button>
        </div>

        <form autocomplete="off" class="space-y-3" @submit.prevent>
          <div class="space-y-1">
            <label class="label">Address</label>
            <input class="input" v-model="dialogAddress" placeholder="192.168.1.100:8080" autocomplete="off" />
          </div>
          <div class="space-y-1">
            <label class="label">Name (optional)</label>
            <input class="input" v-model="dialogName" placeholder="Front Door" autocomplete="off" />
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div class="space-y-1">
              <label class="label">Username</label>
              <input class="input" v-model="dialogUsername" placeholder="admin" autocomplete="off" />
            </div>
            <div class="space-y-1">
              <label class="label">Password</label>
              <input class="input" type="password" v-model="dialogPassword" autocomplete="new-password" />
            </div>
          </div>
          <div class="space-y-1">
            <label class="label">RTSP Port Override (optional)</label>
            <input class="input" v-model="dialogRtspPort" placeholder="554" autocomplete="off" />
            <span class="text-xs text-text-muted">Override the RTSP port returned by the camera, for port-forwarded setups.</span>
          </div>
        </form>

        <div class="flex items-center gap-2">
          <button
            class="btn btn-secondary"
            :disabled="!dialogAddress || dialogProbing"
            @click="probeCamera"
          >
            <div v-if="dialogProbing" class="spinner spinner-sm"></div>
            <i v-else class="ph ph-arrow-clockwise icon-sm"></i>
            {{ dialogProbing ? 'Connecting...' : 'Refresh' }}
          </button>
        </div>

        <div v-if="dialogProbeError" class="toast toast-danger text-sm">
          <i class="ph ph-x-circle icon-sm"></i>
          {{ dialogProbeError }}
        </div>

        <div v-if="dialogProbe" class="space-y-3 border-t border-border pt-3">
          <div class="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
            <span class="text-text-muted">Serial</span>
            <span class="font-mono text-text">{{ dialogProbe.config.serialNumber || '--' }}</span>
            <span class="text-text-muted">Firmware</span>
            <span class="font-mono text-text">{{ dialogProbe.config.firmwareVersion || '--' }}</span>
            <span class="text-text-muted">Capabilities</span>
            <span class="text-text">{{ dialogProbe.capabilities.join(', ') || '--' }}</span>
          </div>
          <div v-if="dialogProbe.streams.length > 0">
            <table class="table text-sm">
              <thead>
                <tr>
                  <th>Profile</th>
                  <th>Codec</th>
                  <th>Resolution</th>
                  <th>FPS</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="s in dialogProbe.streams" :key="s.profile">
                  <td class="font-medium">{{ s.profile }}</td>
                  <td>{{ s.codec }}</td>
                  <td>{{ s.resolution }}</td>
                  <td>{{ s.fps }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="flex justify-end gap-2 border-t border-border pt-4">
          <button class="btn btn-ghost" @click="closeDialog">Cancel</button>
          <button
            class="btn btn-primary"
            :disabled="!dialogAddress || dialogAdding"
            @click="addCamera"
          >
            <div v-if="dialogAdding" class="spinner spinner-sm"></div>
            <i v-else class="ph ph-plus icon-sm"></i>
            Add
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
