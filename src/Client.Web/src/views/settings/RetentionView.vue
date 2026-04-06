<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { ServerSettings, RetentionPolicy } from '@/types/api'

const error = ref('')
const saving = ref(false)
const settings = ref<ServerSettings>({})
const retention = ref<RetentionPolicy>({ mode: 'days', value: 30 })

async function load() {
  try {
    const [s, r] = await Promise.all([
      api.system.settings(),
      api.retention.get(),
    ])
    settings.value = s
    retention.value = r
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function save() {
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

onMounted(load)
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">Retention</h1>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <section class="space-y-4">
      <h2 class="section-subheading">Recording</h2>
      <div class="card p-6 space-y-4">
        <div class="space-y-1">
          <label class="label">Segment Duration (seconds)</label>
          <input class="input" type="number" v-model.number="settings.segmentDuration" placeholder="300" />
        </div>
      </div>
    </section>

    <section class="space-y-4">
      <h2 class="section-subheading">Retention Policy</h2>
      <div class="card p-6 space-y-4">
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

    <button class="btn btn-primary" :disabled="saving" @click="save">
      <div v-if="saving" class="spinner spinner-sm"></div>
      <i v-else class="ph ph-floppy-disk icon-sm"></i>
      {{ saving ? 'Saving...' : 'Save' }}
    </button>
  </div>
</template>
