<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import { useTheme } from '@/stores/theme'
import type { HealthResponse, ServerSettings } from '@/types/api'

const { preference: themePreference } = useTheme()

const error = ref('')
const saving = ref(false)
const health = ref<HealthResponse | null>(null)
const settings = ref<ServerSettings>({})

async function load() {
  try {
    const [h, s] = await Promise.all([
      api.system.health(),
      api.system.settings(),
    ])
    health.value = h
    settings.value = s
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function save() {
  saving.value = true
  error.value = ''
  try {
    await api.system.updateSettings(settings.value)
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    saving.value = false
  }
}

function formatUptime(seconds: number): string {
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const mins = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${mins}m`
  return `${mins}m`
}

onMounted(load)
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">General</h1>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <section v-if="health" class="space-y-4">
      <h2 class="section-subheading">System Health</h2>
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
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

    <section class="space-y-4">
      <h2 class="section-subheading">Appearance</h2>
      <div class="card p-6 space-y-4">
        <div class="space-y-1">
          <label class="label">Theme</label>
          <div class="flex gap-2">
            <button
              class="btn btn-sm"
              :class="themePreference === 'system' ? 'btn-primary' : 'btn-secondary'"
              @click="themePreference = 'system'"
            >
              <i class="ph ph-monitor icon-sm"></i> System
            </button>
            <button
              class="btn btn-sm"
              :class="themePreference === 'light' ? 'btn-primary' : 'btn-secondary'"
              @click="themePreference = 'light'"
            >
              <i class="ph ph-sun icon-sm"></i> Light
            </button>
            <button
              class="btn btn-sm"
              :class="themePreference === 'dark' ? 'btn-primary' : 'btn-secondary'"
              @click="themePreference = 'dark'"
            >
              <i class="ph ph-moon icon-sm"></i> Dark
            </button>
          </div>
        </div>
      </div>
    </section>

    <section class="space-y-4">
      <h2 class="section-subheading">Server</h2>
      <div class="card p-6 space-y-4">
        <div class="space-y-1">
          <label class="label">Server Name</label>
          <input class="input" v-model="settings.serverName" placeholder="My VMS" />
        </div>
        <div class="space-y-1">
          <label class="label">External Endpoint</label>
          <input class="input" v-model="settings.externalEndpoint" placeholder="myhome.ddns.net:443" />
        </div>
        <button class="btn btn-primary" :disabled="saving" @click="save">
          <div v-if="saving" class="spinner spinner-sm"></div>
          <i v-else class="ph ph-floppy-disk icon-sm"></i>
          {{ saving ? 'Saving...' : 'Save' }}
        </button>
      </div>
    </section>
  </div>
</template>
