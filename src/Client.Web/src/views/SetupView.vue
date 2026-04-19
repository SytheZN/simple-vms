<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type { PluginListItem, SettingGroup } from '@/types/api'
import StorageUnavailableBanner from '@/components/setup/StorageUnavailableBanner.vue'

const router = useRouter()
const step = ref<'provider' | 'configure' | 'confirm'>('provider')
const generating = ref(false)
const saving = ref(false)
const error = ref('')
const degraded = ref(false)

const dataPlugins = ref<PluginListItem[]>([])
const selectedPlugin = ref<PluginListItem | null>(null)
const schema = ref<SettingGroup[]>([])
const configValues = ref<Record<string, unknown>>({})
const fieldErrors = ref<Record<string, string>>({})

let pollTimer: ReturnType<typeof setInterval> | undefined

async function checkHealth() {
  try {
    const health = await api.system.health()
    const status = health.status
    degraded.value = status === 'degraded'

    if (status === 'missing-certs' || status === 'degraded' || status === 'starting') return

    if (health.missingSettings && health.missingSettings.length > 0) {
      if (pollTimer) { clearInterval(pollTimer); pollTimer = undefined }
      await router.replace('/setup/complete')
      return
    }

    if (pollTimer) { clearInterval(pollTimer); pollTimer = undefined }
    await router.replace('/')
  } catch {
    // server not ready yet
  }
}

async function loadDataPlugins() {
  try {
    dataPlugins.value = await api.plugins.list('data')
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function selectPlugin(plugin: PluginListItem) {
  selectedPlugin.value = plugin
  error.value = ''
  try {
    schema.value = await api.plugins.configSchema(plugin.id)
    for (const group of schema.value) {
      for (const field of group.fields) {
        if (field.value !== undefined) {
          configValues.value[field.key] = field.value
        } else if (field.defaultValue !== undefined) {
          configValues.value[field.key] = field.defaultValue
        }
      }
    }
    step.value = schema.value.length > 0 ? 'configure' : 'confirm'
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function saveConfig() {
  if (!selectedPlugin.value) return
  saving.value = true
  error.value = ''
  try {
    await api.plugins.updateConfig(selectedPlugin.value.id, configValues.value)
    step.value = 'confirm'
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    saving.value = false
  }
}

async function validateField(key: string) {
  if (!selectedPlugin.value) return
  try {
    await api.plugins.validateField(selectedPlugin.value.id, key, configValues.value[key])
    delete fieldErrors.value[key]
  } catch (e) {
    if (e instanceof ApiError) fieldErrors.value[key] = e.message
  }
}

async function createCerts() {
  generating.value = true
  error.value = ''
  try {
    await api.system.generateCerts()
  } catch (e) {
    generating.value = false
    if (e instanceof ApiError) error.value = e.message
  }
}

function backToProvider() {
  step.value = 'provider'
  selectedPlugin.value = null
  schema.value = []
  configValues.value = {}
  fieldErrors.value = {}
  error.value = ''
}

onMounted(() => {
  loadDataPlugins()
  pollTimer = setInterval(checkHealth, 2000)
})

onUnmounted(() => {
  if (pollTimer) clearInterval(pollTimer)
})
</script>

<template>
  <div class="min-h-screen bg-surface flex items-center justify-center p-8">
    <div class="card p-8 max-w-lg w-full space-y-6">
      <div class="flex items-center gap-3">
        <i class="ph ph-shield-check icon-xl text-primary"></i>
        <h1 class="text-2xl font-bold text-text">Server Setup</h1>
      </div>

      <StorageUnavailableBanner v-if="degraded" />

      <div class="toast toast-warning">
        <i class="ph ph-warning icon-xl"></i>
        <div>
          <span class="font-medium">Is this a new installation?</span>
          <p>If you have previously set up this server and are seeing this page, your data path may be misconfigured (e.g. a network mount that is not mounted). Do not proceed unless you are sure this is a fresh installation.</p>
        </div>
      </div>

      <div v-if="error" class="toast toast-danger">
        <i class="ph ph-x-circle icon-xl"></i>
        <div>
          <span class="font-medium">Error</span>
          <p>{{ error }}</p>
        </div>
      </div>

      <!-- Step 1: Select data provider -->
      <template v-if="step === 'provider'">
        <p class="text-sm text-text-muted">Select a data provider to store server metadata, camera configuration, and recordings index.</p>
        <div class="space-y-2">
          <button
            v-for="plugin in dataPlugins"
            :key="plugin.id"
            class="card p-4 w-full text-left hover:border-primary transition-colors"
            @click="selectPlugin(plugin)"
          >
            <span class="font-medium text-text">{{ plugin.name }}</span>
            <p v-if="plugin.description" class="text-xs text-text-muted mt-1">{{ plugin.description }}</p>
          </button>
          <p v-if="dataPlugins.length === 0" class="text-sm text-text-muted">No data provider plugins found.</p>
        </div>
      </template>

      <!-- Step 2: Configure provider -->
      <template v-if="step === 'configure' && selectedPlugin">
        <div class="flex items-center justify-between">
          <p class="text-sm text-text-muted">Configure <span class="font-medium text-text">{{ selectedPlugin.name }}</span></p>
          <button class="btn btn-ghost btn-sm" @click="backToProvider">
            <i class="ph ph-arrow-left icon-sm"></i> Back
          </button>
        </div>

        <div v-for="group in schema" :key="group.key" class="space-y-3">
          <h3 v-if="schema.length > 1" class="text-sm font-medium text-text">{{ group.label }}</h3>
          <p v-if="group.description" class="text-xs text-text-muted">{{ group.description }}</p>
          <div v-for="field in group.fields" :key="field.key" class="space-y-1">
            <label class="label">
              {{ field.label }}
              <span v-if="field.required" class="text-danger">*</span>
            </label>
            <p v-if="field.description" class="text-xs text-text-muted">{{ field.description }}</p>
            <input
              v-if="field.type === 'string' || field.type === 'path'"
              class="input"
              type="text"
              v-model="configValues[field.key]"
              :placeholder="field.defaultValue?.toString()"
              @blur="validateField(field.key)"
            />
            <input
              v-else-if="field.type === 'password'"
              class="input"
              type="password"
              v-model="configValues[field.key]"
              @blur="validateField(field.key)"
            />
            <input
              v-else-if="field.type === 'int'"
              class="input"
              type="number"
              v-model.number="configValues[field.key]"
              :placeholder="field.defaultValue?.toString()"
              @blur="validateField(field.key)"
            />
            <select
              v-else-if="field.type === 'bool'"
              class="input"
              v-model="configValues[field.key]"
              @change="validateField(field.key)"
            >
              <option :value="true">Yes</option>
              <option :value="false">No</option>
            </select>
            <input
              v-else
              class="input"
              v-model="configValues[field.key]"
              :placeholder="field.defaultValue?.toString()"
              @blur="validateField(field.key)"
            />
            <p v-if="fieldErrors[field.key]" class="text-xs text-danger">{{ fieldErrors[field.key] }}</p>
          </div>
        </div>

        <button
          class="btn btn-primary w-full"
          :disabled="saving || Object.keys(fieldErrors).length > 0"
          @click="saveConfig"
        >
          <div v-if="saving" class="spinner spinner-sm"></div>
          <i v-else class="ph ph-floppy-disk icon-sm"></i>
          {{ saving ? 'Saving...' : 'Save Configuration' }}
        </button>
      </template>

      <!-- Step 3: Create certs -->
      <template v-if="step === 'confirm'">
        <div v-if="selectedPlugin" class="flex items-center justify-between">
          <p class="text-sm text-text-muted">Data provider: <span class="font-medium text-text">{{ selectedPlugin.name }}</span></p>
          <button class="btn btn-ghost btn-sm" @click="backToProvider">
            <i class="ph ph-arrow-left icon-sm"></i> Change
          </button>
        </div>

        <p class="text-sm text-text-muted">
          Clicking the button below will generate the root CA and server certificate, then start the server. This is a one-time operation.
        </p>

        <button
          class="btn btn-primary btn-lg w-full"
          :disabled="generating"
          @click="createCerts"
        >
          <div v-if="generating" class="spinner spinner-sm"></div>
          <i v-else class="ph ph-key icon-md"></i>
          {{ generating ? 'Starting...' : 'Create Certs & Start' }}
        </button>

        <p v-if="generating" class="text-xs text-text-muted text-center">
          Generating certificates and completing server startup. This page will redirect automatically.
        </p>
      </template>
    </div>
  </div>
</template>
