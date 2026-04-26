<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type {
  CameraListItem,
  UpdateCameraRequest,
  CameraConfigSchema,
  CameraConfigValues,
} from '@/types/api'

const route = useRoute()
const router = useRouter()
const cameraId = route.params.id as string

const camera = ref<CameraListItem | null>(null)
const schema = ref<CameraConfigSchema>({ camera: {}, streams: {} })
const values = ref<CameraConfigValues>({ camera: {}, streams: {} })
const initialValues = ref<CameraConfigValues>({ camera: {}, streams: {} })
const loading = ref(true)
const error = ref('')
const success = ref('')

const name = ref('')
const address = ref('')
const username = ref('')
const password = ref('')
const rtspPort = ref('')

const confirmDelete = ref(false)

function toggleBool(scope: Record<string, string>, key: string) {
  scope[key] = scope[key] === 'true' ? 'false' : 'true'
}

function diffConfigValues(): CameraConfigValues {
  const result: CameraConfigValues = { camera: {}, streams: {} }

  for (const [pluginId, fields] of Object.entries(values.value.camera)) {
    const initFields = initialValues.value.camera[pluginId] ?? {}
    const changed: Record<string, string> = {}
    for (const [key, val] of Object.entries(fields))
      if (val !== initFields[key]) changed[key] = val
    if (Object.keys(changed).length > 0) result.camera[pluginId] = changed
  }

  for (const [profile, perPlugin] of Object.entries(values.value.streams)) {
    for (const [pluginId, fields] of Object.entries(perPlugin)) {
      const initFields = initialValues.value.streams[profile]?.[pluginId] ?? {}
      const changed: Record<string, string> = {}
      for (const [key, val] of Object.entries(fields))
        if (val !== initFields[key]) changed[key] = val
      if (Object.keys(changed).length > 0) {
        if (!result.streams[profile]) result.streams[profile] = {}
        result.streams[profile][pluginId] = changed
      }
    }
  }

  return result
}

function hasChanges(diff: CameraConfigValues): boolean {
  return Object.keys(diff.camera).length > 0 || Object.keys(diff.streams).length > 0
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [cameraData, schemaData, valuesData] = await Promise.all([
      api.cameras.get(cameraId),
      api.cameras.configSchema(cameraId),
      api.cameras.configValues(cameraId),
    ])
    camera.value = cameraData
    name.value = cameraData.name
    address.value = cameraData.address
    rtspPort.value = cameraData.config?.rtspPortOverride ?? ''
    schema.value = schemaData
    const seeded: CameraConfigValues = { camera: {}, streams: {} }

    for (const [pluginId, groups] of Object.entries(schemaData.camera)) {
      const existing = valuesData.camera[pluginId] ?? {}
      const filled: Record<string, string> = {}
      for (const group of groups)
        for (const field of group.fields)
          filled[field.key] = existing[field.key] ?? ''
      seeded.camera[pluginId] = filled
    }

    for (const [profile, perPlugin] of Object.entries(schemaData.streams)) {
      seeded.streams[profile] = {}
      for (const [pluginId, groups] of Object.entries(perPlugin)) {
        const existing = valuesData.streams[profile]?.[pluginId] ?? {}
        const filled: Record<string, string> = {}
        for (const group of groups)
          for (const field of group.fields)
            filled[field.key] = existing[field.key] ?? ''
        seeded.streams[profile][pluginId] = filled
      }
    }

    values.value = seeded
    initialValues.value = structuredClone(seeded)
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

async function save() {
  error.value = ''
  success.value = ''
  loading.value = true
  try {
    const body: UpdateCameraRequest = {}
    if (name.value !== camera.value?.name)
      body.name = name.value
    if (address.value !== camera.value?.address)
      body.address = address.value
    if (username.value)
      body.credentials = { username: username.value, password: password.value }
    if (rtspPort.value === '' && camera.value?.config?.rtspPortOverride) {
      body.rtspPortOverride = 0
    } else if (rtspPort.value !== '') {
      const port = parseInt(rtspPort.value, 10)
      if (!isNaN(port) && port > 0)
        body.rtspPortOverride = port
    }

    if (Object.keys(body).length > 0)
      await api.cameras.update(cameraId, body)

    const diff = diffConfigValues()
    if (hasChanges(diff))
      await api.cameras.updateConfig(cameraId, diff)

    success.value = 'Camera updated.'
    username.value = ''
    password.value = ''
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    await load()
  }
}

async function refreshCamera() {
  error.value = ''
  success.value = ''
  loading.value = true
  try {
    await api.cameras.refresh(cameraId)
    success.value = 'Camera details refreshed.'
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    await load()
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
      <div class="card p-6 space-y-3">
        <h2 class="section-subheading">Camera Details</h2>
        <table class="text-sm w-full">
          <tbody>
            <tr>
              <td class="text-text-muted pr-4 py-0.5">Manufacturer</td>
              <td class="text-text py-0.5">{{ camera.config?.manufacturer || '--' }}</td>
            </tr>
            <tr>
              <td class="text-text-muted pr-4 py-0.5">Model</td>
              <td class="text-text py-0.5">{{ camera.config?.model || '--' }}</td>
            </tr>
            <tr>
              <td class="text-text-muted pr-4 py-0.5">Serial</td>
              <td class="font-mono text-text py-0.5">{{ camera.config?.serialNumber || '--' }}</td>
            </tr>
            <tr>
              <td class="text-text-muted pr-4 py-0.5">Firmware</td>
              <td class="font-mono text-text py-0.5">{{ camera.config?.firmwareVersion || '--' }}</td>
            </tr>
            <tr>
              <td class="text-text-muted pr-4 py-0.5">Capabilities</td>
              <td class="text-text py-0.5">{{ camera.capabilities.join(', ') || '--' }}</td>
            </tr>
          </tbody>
        </table>
        <div v-if="camera.streams.length > 0" class="pt-2 border-t border-border">
          <table class="text-sm w-full">
            <thead>
              <tr class="text-text-muted">
                <th class="text-left font-medium py-1">Profile</th>
                <th class="text-left font-medium py-1">Codec</th>
                <th class="text-left font-medium py-1">Resolution</th>
                <th class="text-left font-medium py-1">FPS</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="s in camera.streams" :key="s.profile">
                <td class="text-text py-0.5">{{ s.profile }}</td>
                <td class="text-text py-0.5">{{ s.codec }}</td>
                <td class="text-text py-0.5">{{ s.resolution }}</td>
                <td class="text-text py-0.5">{{ s.fps }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <form autocomplete="off" class="space-y-6" @submit.prevent="save">
        <div class="card p-6 space-y-6">
          <div class="space-y-3">
            <h2 class="section-subheading">General</h2>
            <div class="grid grid-cols-1 sm:grid-cols-[200px_1fr] gap-x-4 gap-y-3 items-center">
              <label class="label">Name</label>
              <input class="input" v-model="name" autocomplete="off" />
              <label class="label">Address</label>
              <input class="input" v-model="address" autocomplete="off" />
            </div>
          </div>

          <div class="space-y-3 border-t border-border pt-4">
            <h2 class="section-subheading">Credentials</h2>
            <p class="text-xs text-text-muted">Leave blank to keep existing credentials.</p>
            <div class="grid grid-cols-1 sm:grid-cols-[200px_1fr] gap-x-4 gap-y-3 items-center">
              <label class="label">Username</label>
              <input class="input" v-model="username" autocomplete="off" />
              <label class="label">Password</label>
              <input class="input" type="password" v-model="password" autocomplete="new-password" />
            </div>
          </div>

          <div class="space-y-3 border-t border-border pt-4">
            <h2 class="section-subheading">Connection</h2>
            <div class="grid grid-cols-1 sm:grid-cols-[200px_1fr] gap-x-4 gap-y-1 items-center">
              <label class="label">RTSP Port Override</label>
              <input class="input" v-model="rtspPort" placeholder="554" autocomplete="off" />
              <p class="sm:col-span-2 text-xs text-text-muted">Override the RTSP port for port-forwarded setups.</p>
            </div>
          </div>
        </div>

        <div v-if="Object.keys(schema.camera).length > 0" class="card p-6 space-y-6">
        <template v-for="(groups, pluginId) in schema.camera" :key="`camera-${pluginId}`">
          <div v-for="group in groups" :key="`camera-${pluginId}-${group.key}`" class="space-y-3 border-t border-border pt-4 first:border-t-0 first:pt-0">
            <h2 class="section-subheading">{{ group.label }}</h2>
            <p v-if="group.description" class="text-xs text-text-muted">{{ group.description }}</p>
            <div
              v-for="field in group.fields"
              :key="field.key"
              class="grid grid-cols-1 sm:grid-cols-[200px_1fr] gap-x-4 gap-y-1 items-center"
            >
              <label class="label">
                {{ field.label }}
                <span v-if="field.required" class="text-danger">*</span>
              </label>
              <div :class="{ 'sm:text-right': field.type === 'boolean' || field.type === 'bool' }">
                <input
                  v-if="field.type === 'string' || field.type === 'path'"
                  class="input" type="text"
                  v-model="values.camera[pluginId][field.key]"
                  :placeholder="field.defaultValue?.toString() ?? ''"
                  autocomplete="off"
                />
                <input
                  v-else-if="field.type === 'password'"
                  class="input" type="password"
                  v-model="values.camera[pluginId][field.key]"
                  autocomplete="new-password"
                />
                <input
                  v-else-if="field.type === 'int' || field.type === 'number'"
                  class="input" type="number"
                  v-model="values.camera[pluginId][field.key]"
                  :placeholder="field.defaultValue?.toString() ?? ''"
                  autocomplete="off"
                />
                <button
                  v-else-if="field.type === 'bool' || field.type === 'boolean'"
                  class="toggle-track" role="switch" type="button"
                  :aria-checked="values.camera[pluginId][field.key] === 'true'"
                  @click="toggleBool(values.camera[pluginId], field.key)"
                >
                  <span class="toggle-knob"></span>
                </button>
                <select
                  v-else-if="field.type === 'select'"
                  class="input"
                  v-model="values.camera[pluginId][field.key]"
                >
                  <option v-for="opt in field.options ?? []" :key="opt.value" :value="opt.value">
                    {{ opt.label }}
                  </option>
                </select>
                <input
                  v-else
                  class="input"
                  v-model="values.camera[pluginId][field.key]"
                  :placeholder="field.defaultValue?.toString() ?? ''"
                  autocomplete="off"
                />
              </div>
              <p
                v-if="field.description"
                class="sm:col-span-2 text-xs text-text-muted"
              >
                {{ field.description }}
              </p>
            </div>
          </div>
        </template>
        </div>

        <div v-for="stream in camera.streams" :key="stream.profile" class="card p-6 space-y-6">
          <div class="space-y-2">
            <h2 class="section-subheading">Stream: {{ stream.profile }}</h2>
            <div class="text-sm flex flex-wrap gap-x-4 gap-y-1">
              <div><span class="text-text-muted">Codec:</span> <span class="text-text">{{ stream.codec }}</span></div>
              <div><span class="text-text-muted">Resolution:</span> <span class="text-text">{{ stream.resolution }}</span></div>
              <div><span class="text-text-muted">FPS:</span> <span class="text-text">{{ stream.fps }}</span></div>
            </div>
          </div>

          <template v-for="(groups, pluginId) in schema.streams[stream.profile] ?? {}" :key="`${stream.profile}-${pluginId}`">
            <div
              v-for="group in groups"
              :key="`${stream.profile}-${pluginId}-${group.key}`"
              class="space-y-3 border-t border-border pt-4"
            >
              <h3 class="section-subheading">{{ group.label }}</h3>
              <p v-if="group.description" class="text-xs text-text-muted">{{ group.description }}</p>
              <div
                v-for="field in group.fields"
                :key="field.key"
                class="grid grid-cols-1 sm:grid-cols-[200px_1fr] gap-x-4 gap-y-1 items-center"
              >
                <label class="label">
                  {{ field.label }}
                  <span v-if="field.required" class="text-danger">*</span>
                </label>
                <div :class="{ 'sm:text-right': field.type === 'boolean' || field.type === 'bool' }">
                  <input
                    v-if="field.type === 'string' || field.type === 'path'"
                    class="input" type="text"
                    v-model="values.streams[stream.profile][pluginId][field.key]"
                    :placeholder="field.defaultValue?.toString() ?? ''"
                    autocomplete="off"
                  />
                  <input
                    v-else-if="field.type === 'password'"
                    class="input" type="password"
                    v-model="values.streams[stream.profile][pluginId][field.key]"
                    autocomplete="new-password"
                  />
                  <input
                    v-else-if="field.type === 'int' || field.type === 'number'"
                    class="input" type="number"
                    v-model="values.streams[stream.profile][pluginId][field.key]"
                    :placeholder="field.defaultValue?.toString() ?? ''"
                    autocomplete="off"
                  />
                  <button
                    v-else-if="field.type === 'bool' || field.type === 'boolean'"
                    class="toggle-track" role="switch" type="button"
                    :aria-checked="values.streams[stream.profile][pluginId][field.key] === 'true'"
                    @click="toggleBool(values.streams[stream.profile][pluginId], field.key)"
                  >
                    <span class="toggle-knob"></span>
                  </button>
                  <select
                    v-else-if="field.type === 'select'"
                    class="input"
                    v-model="values.streams[stream.profile][pluginId][field.key]"
                  >
                    <option v-for="opt in field.options ?? []" :key="opt.value" :value="opt.value">
                      {{ opt.label }}
                    </option>
                  </select>
                  <input
                    v-else
                    class="input"
                    v-model="values.streams[stream.profile][pluginId][field.key]"
                    :placeholder="field.defaultValue?.toString() ?? ''"
                    autocomplete="off"
                  />
                </div>
                <p
                  v-if="field.description && field.type !== 'select'"
                  class="sm:col-span-2 text-xs text-text-muted"
                >
                  {{ field.description }}
                </p>
              </div>
            </div>
          </template>
        </div>

        <div class="flex items-center justify-between">
          <button
            type="button"
            class="btn btn-danger"
            @click="confirmDelete = true"
          >
            <i class="ph ph-trash icon-sm"></i> Delete Camera
          </button>
          <div class="flex items-center gap-2">
            <button type="button" class="btn btn-ghost" @click="refreshCamera">
              <i class="ph ph-arrow-clockwise icon-sm"></i>
              Refresh Camera Details
            </button>
            <button type="submit" class="btn btn-primary">
              <i class="ph ph-floppy-disk icon-sm"></i>
              Save
            </button>
          </div>
        </div>
      </form>

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
