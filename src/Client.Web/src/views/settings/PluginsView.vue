<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, ApiError } from '@/api/client'
import type { PluginListItem } from '@/types/api'

const error = ref('')
const plugins = ref<PluginListItem[]>([])

async function load() {
  try {
    plugins.value = await api.plugins.list()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
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

function statusBadge(status: string): string {
  if (status === 'running') return 'badge-success'
  if (status === 'error') return 'badge-danger'
  return 'badge-neutral'
}

onMounted(load)
</script>

<template>
  <div class="space-y-6">
    <h1 class="section-heading">Plugins</h1>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

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
              <span class="badge" :class="statusBadge(plugin.status)">{{ plugin.status }}</span>
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
  </div>
</template>
