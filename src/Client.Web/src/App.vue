<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '@/api/client'

const route = useRoute()
const router = useRouter()
const ready = ref(false)
const settingsExpanded = ref(false)

const isSettingsPage = computed(() =>
  typeof route.name === 'string' && route.name.startsWith('settings-'))

const settingsOpen = computed(() => settingsExpanded.value || isSettingsPage.value)

onMounted(async () => {
  if (route.name === 'setup') {
    ready.value = true
    return
  }

  while (true) {
    try {
      const health = await api.system.health()
      if (health.status === 'missing-certs') {
        await router.replace('/setup')
        break
      }
      if (health.status !== 'starting') break
    } catch {
    }
    await new Promise(resolve => setTimeout(resolve, 1000))
  }
  ready.value = true
})
</script>

<template>
  <div v-if="!ready" class="min-h-screen bg-surface flex items-center justify-center">
    <div class="spinner spinner-lg"></div>
  </div>
  <div v-else-if="route.name === 'setup'" class="min-h-screen bg-surface font-sans">
    <router-view />
  </div>
  <div v-else class="min-h-screen bg-surface font-sans flex">
    <nav class="nav-sidebar">
      <div class="text-lg font-bold text-primary px-3 py-3 mb-2">
        <i class="ph ph-shield-check icon-md"></i> VMS
      </div>
      <router-link to="/" class="nav-link" active-class="nav-link-active" exact>
        <i class="ph ph-squares-four icon-sm"></i> Gallery
      </router-link>
      <router-link to="/events" class="nav-link" active-class="nav-link-active">
        <i class="ph ph-lightning icon-sm"></i> Events
      </router-link>
      <router-link to="/clients" class="nav-link" active-class="nav-link-active">
        <i class="ph ph-devices icon-sm"></i> Clients
      </router-link>
      <a href="#" class="nav-link" :class="{ 'nav-link-active': isSettingsPage }" @click.prevent="settingsExpanded = !settingsExpanded">
        <i class="ph ph-gear icon-sm"></i> Settings
        <i class="ph ph-caret-down icon-sm nav-link-toggle" :class="{ 'nav-link-toggle-open': settingsOpen }"></i>
      </a>
      <div v-if="settingsOpen" class="nav-children">
        <router-link to="/settings/general" class="nav-child" active-class="nav-child-active">
          <i class="ph ph-faders icon-sm"></i> General
        </router-link>
        <router-link to="/settings/cameras" class="nav-child" active-class="nav-child-active">
          <i class="ph ph-video-camera icon-sm"></i> Cameras
        </router-link>
        <router-link to="/settings/storage" class="nav-child" active-class="nav-child-active">
          <i class="ph ph-hard-drives icon-sm"></i> Storage
        </router-link>
        <router-link to="/settings/retention" class="nav-child" active-class="nav-child-active">
          <i class="ph ph-clock-countdown icon-sm"></i> Retention
        </router-link>
        <router-link to="/settings/plugins" class="nav-child" active-class="nav-child-active">
          <i class="ph ph-puzzle-piece icon-sm"></i> Plugins
        </router-link>
      </div>
    </nav>
    <main class="flex-1 p-6 overflow-y-auto">
      <router-view />
    </main>
  </div>
</template>
