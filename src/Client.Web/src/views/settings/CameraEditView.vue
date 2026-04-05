<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type { CameraListItem, UpdateCameraRequest, UpdateStreamConfig } from '@/types/api'

const route = useRoute()
const router = useRouter()
const cameraId = route.params.id as string

const camera = ref<CameraListItem | null>(null)
const loading = ref(true)
const saving = ref(false)
const error = ref('')
const success = ref('')

const name = ref('')
const username = ref('')
const password = ref('')
const rtspPort = ref('')
const segmentDuration = ref('')
const retentionMode = ref('default')
const retentionValue = ref('')
const streams = ref<{ profile: string; codec: string; resolution: string; fps: number; recordingEnabled: boolean }[]>([])

const confirmDelete = ref(false)

async function load() {
  loading.value = true
  error.value = ''
  try {
    camera.value = await api.cameras.get(cameraId)
    name.value = camera.value.name
    rtspPort.value = camera.value.config?.rtspPortOverride ?? ''
    segmentDuration.value = camera.value.segmentDuration?.toString() ?? ''
    retentionMode.value = camera.value.retentionMode ?? 'default'
    retentionValue.value = camera.value.retentionValue?.toString() ?? ''
    streams.value = camera.value.streams.map(s => ({ ...s }))
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    const body: UpdateCameraRequest = {}
    if (name.value !== camera.value?.name)
      body.name = name.value
    if (username.value)
      body.credentials = { username: username.value, password: password.value }

    const port = parseInt(rtspPort.value, 10)
    body.rtspPortOverride = (!isNaN(port) && port > 0) ? port : undefined

    const dur = parseInt(segmentDuration.value, 10)
    body.segmentDuration = (!isNaN(dur) && dur > 0) ? dur : undefined

    body.retention = retentionMode.value !== 'default'
      ? { mode: retentionMode.value, value: parseInt(retentionValue.value, 10) || 0 }
      : undefined

    const streamUpdates: UpdateStreamConfig[] = streams.value.map(s => ({
      profile: s.profile,
      recordingEnabled: s.recordingEnabled,
    }))
    const changed = camera.value?.streams.some((orig, i) =>
      orig.recordingEnabled !== streamUpdates[i]?.recordingEnabled)
    if (changed) body.streams = streamUpdates

    await api.cameras.update(cameraId, body)
    success.value = 'Camera updated.'
    username.value = ''
    password.value = ''
    await load()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    saving.value = false
  }
}

async function deleteCamera() {
  try {
    await api.cameras.delete(cameraId)
    router.push('/settings/cameras')
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

onMounted(load)
</script>

<template>
  <div class="space-y-6">
    <button class="btn btn-ghost btn-sm mb-2" @click="router.push('/settings/cameras')">
      <i class="ph ph-arrow-left icon-sm"></i> Cameras
    </button>
    <h1 class="section-heading">{{ camera?.name ?? 'Camera' }}</h1>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <div v-if="success" class="toast toast-success">
      <i class="ph ph-check-circle icon-xl"></i>
      <p>{{ success }}</p>
    </div>

    <div v-if="loading" class="flex justify-center py-12">
      <div class="spinner spinner-lg"></div>
    </div>

    <template v-else-if="camera">
      <form autocomplete="off" class="space-y-6" @submit.prevent="save">
        <div class="card p-6 space-y-4">
          <h2 class="section-subheading">General</h2>
          <div class="grid grid-cols-2 gap-4">
            <div class="space-y-1">
              <label class="label">Name</label>
              <input class="input" v-model="name" autocomplete="off" />
            </div>
            <div class="space-y-1">
              <label class="label">Address</label>
              <input class="input" :value="camera.address" disabled />
            </div>
          </div>
        </div>

        <div class="card p-6 space-y-4">
          <h2 class="section-subheading">Credentials</h2>
          <p class="text-xs text-text-muted">Leave blank to keep existing credentials.</p>
          <div class="grid grid-cols-2 gap-4">
            <div class="space-y-1">
              <label class="label">Username</label>
              <input class="input" v-model="username" autocomplete="off" />
            </div>
            <div class="space-y-1">
              <label class="label">Password</label>
              <input class="input" type="password" v-model="password" autocomplete="new-password" />
            </div>
          </div>
        </div>

        <div class="card p-6 space-y-4">
          <h2 class="section-subheading">Connection</h2>
          <div class="grid grid-cols-2 gap-4">
            <div class="space-y-1">
              <label class="label">RTSP Port Override</label>
              <input class="input" v-model="rtspPort" placeholder="554" autocomplete="off" />
              <span class="text-xs text-text-muted">Override the RTSP port for port-forwarded setups.</span>
            </div>
            <div class="space-y-1">
              <label class="label">Segment Duration (seconds)</label>
              <input class="input" v-model="segmentDuration" placeholder="300" autocomplete="off" />
            </div>
          </div>
        </div>

        <div class="card p-6 space-y-4">
          <h2 class="section-subheading">Streams</h2>
          <div v-if="streams.length === 0" class="text-sm text-text-muted">No streams configured.</div>
          <table v-else class="table">
            <thead>
              <tr>
                <th>Profile</th>
                <th>Codec</th>
                <th>Resolution</th>
                <th>FPS</th>
                <th>Recording</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="s in streams" :key="s.profile">
                <td class="font-medium">{{ s.profile }}</td>
                <td>{{ s.codec }}</td>
                <td>{{ s.resolution }}</td>
                <td>{{ s.fps }}</td>
                <td>
                  <button
                    class="toggle-track"
                    role="switch"
                    :aria-checked="s.recordingEnabled"
                    @click="s.recordingEnabled = !s.recordingEnabled"
                  >
                    <span class="toggle-knob"></span>
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="card p-6 space-y-4">
          <h2 class="section-subheading">Retention</h2>
          <div class="grid grid-cols-2 gap-4">
            <div class="space-y-1">
              <label class="label">Mode</label>
              <select class="input" v-model="retentionMode">
                <option value="default">Default (inherit)</option>
                <option value="days">Days</option>
                <option value="bytes">Bytes</option>
                <option value="percent">Percent</option>
              </select>
            </div>
            <div v-if="retentionMode !== 'default'" class="space-y-1">
              <label class="label">Value</label>
              <input class="input" v-model="retentionValue" autocomplete="off" />
            </div>
          </div>
        </div>

        <div class="flex items-center justify-between">
          <button
            type="button"
            class="btn btn-ghost text-danger"
            @click="confirmDelete = true"
          >
            <i class="ph ph-trash icon-sm"></i> Delete Camera
          </button>
          <button type="submit" class="btn btn-primary" :disabled="saving">
            <div v-if="saving" class="spinner spinner-sm"></div>
            <i v-else class="ph ph-floppy-disk icon-sm"></i>
            Save
          </button>
        </div>
      </form>

      <!-- Delete confirmation -->
      <div v-if="confirmDelete" class="modal-container" @click.self="confirmDelete = false">
        <div class="modal-backdrop"></div>
        <div class="relative card p-6 max-w-sm w-full shadow-modal space-y-4">
          <h2 class="section-subheading">Delete Camera</h2>
          <p class="text-sm text-text-muted">
            Are you sure you want to delete <strong>{{ camera.name }}</strong>? This will remove all associated streams and recordings.
          </p>
          <div class="flex justify-end gap-2">
            <button class="btn btn-ghost" @click="confirmDelete = false">Cancel</button>
            <button class="btn btn-danger" @click="deleteCamera">Delete</button>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
