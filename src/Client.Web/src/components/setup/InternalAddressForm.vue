<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { LOOPBACK_HOSTS, normalizeServerAddress } from '@/lib/validation/networkEndpoints'

const props = defineProps<{
  initialServerName?: string
  initialInternalEndpoint?: string
  serverListenPort: number
  saving?: boolean
}>()

const emit = defineEmits<{
  (e: 'save', payload: { serverName: string; internalEndpoint: string }): void
}>()

const serverName = ref(props.initialServerName ?? '')
const internalEndpoint = ref(props.initialInternalEndpoint ?? '')
const error = ref('')

function isUsableHost(host: string): boolean {
  const lower = host.toLowerCase()
  if (LOOPBACK_HOSTS.has(lower)) return false
  if (lower.startsWith('127.')) return false
  return true
}

function prefill() {
  if (internalEndpoint.value) return
  const host = window.location.hostname
  if (host && isUsableHost(host)) internalEndpoint.value = host
}

const preview = computed(() => {
  const result = normalizeServerAddress(internalEndpoint.value, {
    serverListenPort: props.serverListenPort
  })
  if (!('value' in result)) return null
  return isUsableHost(result.value.host) ? result.value : null
})

function submit() {
  const result = normalizeServerAddress(internalEndpoint.value, {
    serverListenPort: props.serverListenPort
  })
  if ('error' in result) {
    error.value = result.error
    return
  }
  if (!isUsableHost(result.value.host)) {
    error.value = `'${result.value.host}' is a loopback or container-only address; clients on other devices can't reach it.`
    return
  }
  internalEndpoint.value = result.value.url
  error.value = ''
  emit('save', {
    serverName: serverName.value.trim(),
    internalEndpoint: result.value.url
  })
}

onMounted(prefill)
defineExpose({ submit })
</script>

<template>
  <form class="space-y-4" @submit.prevent="submit">
    <div class="space-y-1">
      <label class="label">Server name</label>
      <input class="input" v-model="serverName" placeholder="My VMS" />
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
        v-model="internalEndpoint"
        placeholder="e.g. 192.168.1.50, vms.local, or https://myvms.example.com"
      />
      <p v-if="preview" class="text-xs text-text-muted">
        Click
        <a :href="preview.url" target="_blank" rel="noopener noreferrer" class="underline inline-flex items-center gap-1">
          here <i class="ph ph-arrow-square-out icon-xs"></i>
        </a>
        to test. If the gallery doesn't open, check the value and try again.
      </p>
    </div>
    <p v-if="error" class="text-xs text-danger">{{ error }}</p>
    <button class="btn btn-primary w-full" type="submit" :disabled="saving">
      <div v-if="saving" class="spinner spinner-sm"></div>
      <i v-else class="ph ph-floppy-disk icon-sm"></i>
      {{ saving ? 'Saving...' : 'Save' }}
    </button>
  </form>
</template>
