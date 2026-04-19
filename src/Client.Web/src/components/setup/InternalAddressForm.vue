<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { LOOPBACK_HOSTS, splitHost } from '@/lib/validation/networkEndpoints'

const props = defineProps<{
  initialServerName?: string
  initialInternalEndpoint?: string
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

function submit() {
  const trimmed = internalEndpoint.value.trim()
  if (!trimmed) {
    error.value = 'Enter the address other devices on your network should use to reach this server.'
    return
  }
  const host = splitHost(trimmed)
  if (!isUsableHost(host)) {
    error.value = `'${host}' is a loopback or container-only address; clients on other devices can't reach it.`
    return
  }
  error.value = ''
  emit('save', {
    serverName: serverName.value.trim(),
    internalEndpoint: trimmed
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
        placeholder="e.g. vms.local or 192.168.1.50"
      />
    </div>
    <p v-if="error" class="text-xs text-danger">{{ error }}</p>
    <button class="btn btn-primary w-full" type="submit" :disabled="saving">
      <div v-if="saving" class="spinner spinner-sm"></div>
      <i v-else class="ph ph-floppy-disk icon-sm"></i>
      {{ saving ? 'Saving...' : 'Save' }}
    </button>
  </form>
</template>
