<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { ServerSettings, RetentionPolicy, SystemEventRetention } from '@/types/api'

const error = ref('')
const saving = ref(false)
const settings = ref<ServerSettings>({})
const retention = ref<RetentionPolicy>({ mode: 'days', value: 30, minFreeSpaceGb: 2.0 })
const systemEventRetention = ref<SystemEventRetention>({ days: 180 })

async function load() {
  try {
    const [s, r, se] = await Promise.all([
      api.system.settings(),
      api.retention.get(),
      api.retention.getSystemEvents(),
    ])
    settings.value = s
    retention.value = r
    systemEventRetention.value = se
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
    await api.retention.updateSystemEvents(systemEventRetention.value)
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
      <h2 class="section-subheading">Recording Retention Policy</h2>
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
        <div class="space-y-1">
          <label class="label">Minimum free space (GB)</label>
          <input class="input" type="number" step="0.1" min="0.5"
                 v-model.number="retention.minFreeSpaceGb" />
        </div>

        <div class="toast toast-warning">
          <i class="ph ph-warning icon-xl"></i>
          <div>
            <span class="font-medium">Warning</span>
            <p>Oldest recordings are trimmed regardless of retention policy when free space drops below this threshold. Recording halts on all streams if free space falls below 0.2 GB, and resumes once free space returns above the threshold.</p>
          </div>
        </div>
      </div>
    </section>

    <section class="space-y-4">
      <h2 class="section-subheading">System Event Retention</h2>
      <div class="card p-6 space-y-4">
        <div class="space-y-1">
          <label class="label">Days</label>
          <input class="input" type="number" v-model.number="systemEventRetention.days" placeholder="180" />
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
