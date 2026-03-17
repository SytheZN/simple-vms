<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'

const router = useRouter()
const generating = ref(false)
const error = ref('')

let pollTimer: ReturnType<typeof setInterval> | undefined

async function checkHealth() {
  try {
    const health = await api.system.health()
    if (health.status !== 'missing-certs') {
      router.replace('/')
    }
  } catch (e) {
    if (e instanceof ApiError) {
      error.value = e.message
    }
  }
}

async function createCerts() {
  generating.value = true
  error.value = ''
  try {
    await api.system.generateCerts()
  } catch (e) {
    generating.value = false
    if (e instanceof ApiError) {
      error.value = e.message
    }
  }
}

onMounted(() => {
  checkHealth()
  pollTimer = setInterval(checkHealth, 2000)
})

onUnmounted(() => {
  clearInterval(pollTimer)
})
</script>

<template>
  <div class="min-h-screen bg-surface flex items-center justify-center p-8">
    <div class="card p-8 max-w-lg w-full space-y-6">
      <div class="flex items-center gap-3">
        <i class="ph ph-shield-check icon-xl text-primary"></i>
        <h1 class="text-2xl font-bold text-text">Server Setup</h1>
      </div>

      <div class="toast toast-warning">
        <i class="ph ph-warning icon-xl"></i>
        <div>
          <span class="font-medium">Is this a new installation?</span>
          <p>If you have previously set up this server and are seeing this page, your data path may be misconfigured (e.g. a network mount that is not mounted). Do not proceed unless you are sure this is a fresh installation.</p>
        </div>
      </div>

      <p class="text-sm text-text-muted">
        Clicking the button below will generate the root CA and server certificate. This is a one-time operation that initializes the server's identity. All client enrollments depend on these certificates.
      </p>

      <div v-if="error" class="toast toast-danger">
        <i class="ph ph-x-circle icon-xl"></i>
        <div>
          <span class="font-medium">Error</span>
          <p>{{ error }}</p>
        </div>
      </div>

      <button
        class="btn btn-primary btn-lg w-full"
        :disabled="generating"
        @click="createCerts"
      >
        <div v-if="generating" class="spinner spinner-sm"></div>
        <i v-else class="ph ph-key icon-md"></i>
        {{ generating ? 'Generating...' : 'Create Certs' }}
      </button>

      <p v-if="generating" class="text-xs text-text-muted text-center">
        Generating certificates and completing server startup. This page will redirect automatically.
      </p>
    </div>
  </div>
</template>
