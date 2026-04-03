<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type { PluginListItem, SettingGroup } from '@/types/api'

const route = useRoute()
const router = useRouter()
const pluginId = route.params.id as string

const plugin = ref<PluginListItem | null>(null)
const schema = ref<SettingGroup[]>([])
const values = ref<Record<string, unknown>>({})
const fieldErrors = ref<Record<string, string>>({})
const loading = ref(true)
const saving = ref(false)
const error = ref('')
const success = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    plugin.value = await api.plugins.get(pluginId)
    schema.value = await api.plugins.configSchema(pluginId)
    const existing = await api.plugins.configValues(pluginId)
    for (const group of schema.value) {
      for (const field of group.fields) {
        values.value[field.key] = existing[field.key] ?? field.defaultValue ?? ''
      }
    }
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  } finally {
    loading.value = false
  }
}

async function validateField(key: string) {
  try {
    await api.plugins.validateField(pluginId, key, values.value[key])
    delete fieldErrors.value[key]
  } catch (e) {
    if (e instanceof ApiError) fieldErrors.value[key] = e.message
  }
}

async function save() {
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    await api.plugins.updateConfig(pluginId, values.value)
    success.value = 'Settings saved.'
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
    <button class="btn btn-ghost btn-sm mb-2" @click="router.push('/settings/plugins')">
      <i class="ph ph-arrow-left icon-sm"></i> Plugins
    </button>
    <h1 class="section-heading">{{ plugin?.name ?? 'Plugin' }} Settings</h1>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <div v-if="success" class="toast toast-success">
      <i class="ph ph-check-circle icon-xl"></i>
      <p>{{ success }}</p>
    </div>

    <div v-if="loading" class="flex justify-center py-12">
      <div class="spinner spinner-lg"></div>
    </div>

    <template v-else-if="schema.length > 0">
      <form autocomplete="off" class="space-y-6" @submit.prevent="save">
        <div v-for="group in schema" :key="group.key" class="card p-6 space-y-4">
          <h2 class="section-subheading">{{ group.label }}</h2>
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
              v-model="values[field.key]"
              :placeholder="field.defaultValue?.toString()"
              autocomplete="off"
              @blur="validateField(field.key)"
            />
            <input
              v-else-if="field.type === 'password'"
              class="input"
              type="password"
              v-model="values[field.key]"
              autocomplete="new-password"
              @blur="validateField(field.key)"
            />
            <input
              v-else-if="field.type === 'int'"
              class="input"
              type="number"
              v-model.number="values[field.key]"
              :placeholder="field.defaultValue?.toString()"
              autocomplete="off"
              @blur="validateField(field.key)"
            />
            <select
              v-else-if="field.type === 'bool'"
              class="input"
              v-model="values[field.key]"
              @change="validateField(field.key)"
            >
              <option :value="true">Yes</option>
              <option :value="false">No</option>
            </select>
            <input
              v-else
              class="input"
              v-model="values[field.key]"
              :placeholder="field.defaultValue?.toString()"
              autocomplete="off"
              @blur="validateField(field.key)"
            />
            <p v-if="fieldErrors[field.key]" class="text-xs text-danger">{{ fieldErrors[field.key] }}</p>
          </div>
        </div>

        <div class="flex justify-end">
          <button type="submit" class="btn btn-primary" :disabled="saving || Object.keys(fieldErrors).length > 0">
            <div v-if="saving" class="spinner spinner-sm"></div>
            <i v-else class="ph ph-floppy-disk icon-sm"></i>
            Save
          </button>
        </div>
      </form>
    </template>

    <div v-else class="text-sm text-text-muted">This plugin has no configurable settings.</div>
  </div>
</template>
