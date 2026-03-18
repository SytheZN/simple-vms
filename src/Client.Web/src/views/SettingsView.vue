<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type {
  ServerSettings,
  RetentionPolicy,
  HealthResponse,
  StorageResponse,
  PluginListItem,
} from '@/types/api'

const error = ref('')
const saving = ref(false)
const section = ref('health')

const settings = ref<ServerSettings>({})
const retention = ref<RetentionPolicy>({ mode: 'days', value: 30 })
const health = ref<HealthResponse | null>(null)
const storage = ref<StorageResponse | null>(null)
const plugins = ref<PluginListItem[]>([])

async function loadAll() {
  try {
    const [s, r, h, st, p] = await Promise.all([
      api.system.settings(),
      api.retention.get(),
      api.system.health(),
      api.system.storage(),
      api.plugins.list(),
    ])
    settings.value = s
    retention.value = r
    health.value = h
    storage.value = st
    plugins.value = p
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function saveSettings() {
  saving.value = true
  error.value = ''
  try {
    await api.system.updateSettings(settings.value)
    await api.retention.update(retention.value)
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    saving.value = false
  }
}

async function startPlugin(id: string) {
  try {
    await api.plugins.start(id)
    plugins.value = await api.plugins.list()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function stopPlugin(id: string) {
  try {
    await api.plugins.stop(id)
    plugins.value = await api.plugins.list()
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

function formatUptime(seconds: number): string {
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const mins = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${mins}m`
  return `${mins}m`
}

function pluginStatusBadge(status: string): string {
  if (status === 'running') return 'badge-success'
  if (status === 'error') return 'badge-danger'
  return 'badge-neutral'
}

onMounted(loadAll)
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between">
      <h1 class="section-heading">Settings</h1>
      <select class="input w-auto" v-model="section">
        <option value="health">Health</option>
        <option value="server">Server</option>
        <option value="defaults">Defaults</option>
        <option value="plugins">Plugins</option>
      </select>
    </div>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <!-- Health -->
    <template v-if="section === 'health'">
      <section v-if="health" class="space-y-4">
        <h2 class="section-subheading">System Health</h2>
        <div class="grid grid-cols-3 gap-4">
          <div class="card p-4 space-y-1">
            <span class="section-subheading">Status</span>
            <span class="block text-2xl font-bold text-text capitalize">{{ health.status }}</span>
          </div>
          <div class="card p-4 space-y-1">
            <span class="section-subheading">Uptime</span>
            <span class="block text-2xl font-bold text-text">{{ formatUptime(health.uptime) }}</span>
          </div>
          <div class="card p-4 space-y-1">
            <span class="section-subheading">Version</span>
            <span class="block text-2xl font-bold font-mono text-text">{{ health.version }}</span>
          </div>
        </div>
      </section>

      <section v-if="storage && storage.stores.length > 0" class="space-y-4">
        <h2 class="section-subheading">Storage</h2>
        <div class="grid grid-cols-3 gap-4">
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
      </section>
    </template>

    <!-- Server -->
    <template v-if="section === 'server'">
      <section class="space-y-4">
        <h2 class="section-subheading">Server</h2>
        <div class="card p-6 space-y-4 max-w-lg">
          <div class="space-y-1">
            <label class="label">Server Name</label>
            <input class="input" v-model="settings.serverName" placeholder="My VMS" />
          </div>
          <div class="space-y-1">
            <label class="label">External Endpoint</label>
            <input class="input" v-model="settings.externalEndpoint" placeholder="myhome.ddns.net:443" />
          </div>
          <div class="space-y-1">
            <label class="label">Discovery Subnets</label>
            <input
              class="input"
              :value="settings.discoverySubnets?.join(', ')"
              @change="settings.discoverySubnets = ($event.target as HTMLInputElement).value.split(',').map(s => s.trim()).filter(Boolean)"
              placeholder="192.168.1.0/24"
            />
          </div>
          <button class="btn btn-primary" :disabled="saving" @click="saveSettings">
            <div v-if="saving" class="spinner spinner-sm"></div>
            <i v-else class="ph ph-floppy-disk icon-sm"></i>
            {{ saving ? 'Saving...' : 'Save' }}
          </button>
        </div>
      </section>
    </template>

    <!-- Defaults -->
    <template v-if="section === 'defaults'">
      <section class="space-y-4">
        <h2 class="section-subheading">Recording</h2>
        <div class="card p-6 space-y-4 max-w-lg">
          <div class="space-y-1">
            <label class="label">Segment Duration (seconds)</label>
            <input class="input" type="number" v-model.number="settings.segmentDuration" placeholder="300" />
          </div>
        </div>
      </section>

      <section class="space-y-4">
        <h2 class="section-subheading">Retention</h2>
        <div class="card p-6 space-y-4 max-w-lg">
          <div class="space-y-1">
            <label class="label">Mode</label>
            <select class="input" v-model="retention.mode">
              <option value="days">Days</option>
              <option value="bytes">Bytes</option>
              <option value="percent">Percent</option>
            </select>
          </div>
          <div class="space-y-1">
            <label class="label">Value</label>
            <input class="input" type="number" v-model.number="retention.value" />
          </div>
        </div>
      </section>

      <section class="space-y-4">
        <h2 class="section-subheading">Default Credentials</h2>
        <div class="card p-6 space-y-4 max-w-lg">
          <div class="space-y-1">
            <label class="label">Username</label>
            <input class="input" :value="settings.defaultCredentials?.username" @input="settings.defaultCredentials = { username: ($event.target as HTMLInputElement).value, password: settings.defaultCredentials?.password ?? '' }" placeholder="admin" />
          </div>
          <div class="space-y-1">
            <label class="label">Password</label>
            <input class="input" type="password" :value="settings.defaultCredentials?.password" @input="settings.defaultCredentials = { username: settings.defaultCredentials?.username ?? '', password: ($event.target as HTMLInputElement).value }" />
          </div>
        </div>
      </section>

      <button class="btn btn-primary" :disabled="saving" @click="saveSettings">
        <div v-if="saving" class="spinner spinner-sm"></div>
        <i v-else class="ph ph-floppy-disk icon-sm"></i>
        {{ saving ? 'Saving...' : 'Save' }}
      </button>
    </template>

    <!-- Plugins -->
    <template v-if="section === 'plugins'">
      <section class="space-y-4">
        <h2 class="section-subheading">Plugins</h2>
        <div v-if="plugins.length === 0" class="text-sm text-text-muted">No plugins installed.</div>
        <div v-else class="card overflow-hidden">
          <table class="table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Version</th>
                <th>Status</th>
                <th>Extensions</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="plugin in plugins" :key="plugin.id">
                <td>
                  <span class="font-medium">{{ plugin.name }}</span>
                  <p v-if="plugin.description" class="text-xs text-text-muted">{{ plugin.description }}</p>
                </td>
                <td class="font-mono text-text-muted">{{ plugin.version }}</td>
                <td>
                  <span class="badge" :class="pluginStatusBadge(plugin.status)">{{ plugin.status }}</span>
                </td>
                <td class="text-text-muted text-xs">{{ plugin.extensionPoints.join(', ') }}</td>
                <td class="text-right">
                  <template v-if="plugin.userStartable">
                    <button
                      v-if="plugin.status === 'running'"
                      class="btn btn-ghost btn-sm"
                      @click="stopPlugin(plugin.id)"
                    >
                      <i class="ph ph-stop icon-sm"></i> Stop
                    </button>
                    <button
                      v-else
                      class="btn btn-ghost btn-sm"
                      @click="startPlugin(plugin.id)"
                    >
                      <i class="ph ph-play icon-sm"></i> Start
                    </button>
                  </template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </template>
  </div>
</template>
