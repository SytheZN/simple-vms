<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type { HealthResponse, ServerSettings } from '@/types/api'
import InternalAddressForm from '@/components/setup/InternalAddressForm.vue'
import RemoteAccessSection from '@/components/settings/RemoteAccessSection.vue'
import { validateHostOrIp } from '@/lib/validation/networkEndpoints'

const router = useRouter()
const saving = ref(false)
const loading = ref(true)
const error = ref('')
const health = ref<HealthResponse | null>(null)
const settings = ref<ServerSettings>({})
const remoteValid = ref(true)

const needsInternal = computed(() =>
  health.value?.missingSettings?.includes('internalEndpoint') ?? false)

const serverListenPort = computed(() => health.value!.httpPort)

const needsLegacyMigration = computed(() =>
  health.value?.missingSettings?.includes('legacyExternalEndpoint') ?? false)

const needsRemoteAccess = computed(() => needsLegacyMigration.value)

const isMigration = computed(() => needsLegacyMigration.value)

const title = computed(() =>
  isMigration.value ? 'Finish the upgrade' : 'Finish setup')

const intro = computed(() =>
  isMigration.value
    ? 'The server has been upgraded and needs a little configuration before it can continue.'
    : 'A few settings are still required before the server can go live.')

function splitLegacy(legacy: string): { host: string; port?: number } {
  const colon = legacy.lastIndexOf(':')
  if (colon < 0) return { host: legacy }
  const portStr = legacy.slice(colon + 1)
  const port = Number(portStr)
  return Number.isInteger(port) && port > 0
    ? { host: legacy.slice(0, colon), port }
    : { host: legacy }
}

async function load() {
  try {
    const [h, s] = await Promise.all([
      api.system.health(),
      api.system.settings()
    ])
    health.value = h

    const seeded: ServerSettings = { ...s }
    if (s.legacyExternalEndpoint && !s.externalHost && !s.externalPort) {
      const split = splitLegacy(s.legacyExternalEndpoint)
      seeded.mode = 'manual'
      seeded.externalHost = split.host
      seeded.externalPort = split.port
    } else if (!seeded.mode) {
      seeded.mode = 'none'
    }
    settings.value = seeded
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

const internalError = computed(() => {
  const v = settings.value.internalEndpoint?.trim() ?? ''
  if (!v) return ''
  const r = validateHostOrIp(v, { allowPort: true, fieldLabel: 'Internal address' })
  return r.valid ? '' : r.error ?? ''
})

const canSubmit = computed(() => {
  if (needsInternal.value && !(settings.value.internalEndpoint?.trim())) return false
  if (internalError.value) return false
  if (!remoteValid.value) return false
  return true
})

async function handleInternalSave(payload: { serverName: string; internalEndpoint: string }) {
  settings.value = {
    ...settings.value,
    serverName: payload.serverName || settings.value.serverName,
    internalEndpoint: payload.internalEndpoint
  }
  await submit()
}

async function submit() {
  saving.value = true
  error.value = ''
  try {
    await api.system.updateSettings(settings.value)
    await router.replace('/')
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="min-h-screen bg-surface flex items-center justify-center p-8">
    <div class="card p-8 max-w-2xl w-full space-y-6">
      <div class="flex items-center gap-3">
        <i class="ph ph-sparkle icon-xl text-primary"></i>
        <h1 class="text-2xl font-bold text-text">{{ title }}</h1>
      </div>

      <p class="text-sm text-text-muted">{{ intro }}</p>

      <div v-if="error" class="toast toast-danger">
        <i class="ph ph-x-circle icon-xl"></i>
        <div>
          <span class="font-medium">Error</span>
          <p>{{ error }}</p>
        </div>
      </div>

      <div v-if="loading" class="flex justify-center py-8">
        <div class="spinner spinner-lg"></div>
      </div>

      <template v-else>
        <section v-if="needsInternal && !needsRemoteAccess">
          <h2 class="section-subheading mb-2">Network</h2>
          <InternalAddressForm
            :saving="saving"
            :server-listen-port="serverListenPort"
            @save="handleInternalSave"
          />
        </section>

        <template v-else>
          <section v-if="needsInternal" class="space-y-4">
            <h2 class="section-subheading">Server identity</h2>
            <div class="card p-6 space-y-4">
              <div class="space-y-1">
                <label class="label">Server name</label>
                <input class="input" v-model="settings.serverName" placeholder="My VMS" />
              </div>
              <div class="space-y-1">
                <label class="label">
                  Internal address <span class="text-danger">*</span>
                </label>
                <p class="text-xs text-text-muted">
                  The hostname or IP that other devices on your local network will use to reach this server.
                </p>
                <input
                  class="input"
                  v-model="settings.internalEndpoint"
                  placeholder="vms.local or 192.168.1.50"
                />
                <p v-if="internalError" class="text-xs text-danger">{{ internalError }}</p>
              </div>
            </div>
          </section>

          <RemoteAccessSection
            v-if="needsRemoteAccess"
            v-model="settings"
            :tunnel-port-hint="health?.tunnelPort"
            @validity="remoteValid = $event"
          />

          <button class="btn btn-primary w-full" :disabled="saving || !canSubmit" @click="submit">
            <div v-if="saving" class="spinner spinner-sm"></div>
            <i v-else class="ph ph-floppy-disk icon-sm"></i>
            {{ saving ? 'Saving...' : 'Save' }}
          </button>
        </template>
      </template>
    </div>
  </div>
</template>
