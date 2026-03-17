<script setup lang="ts">
import { useRoute } from 'vue-router'

const route = useRoute()
</script>

<template>
  <div v-if="route.name === 'setup'" class="min-h-screen bg-surface font-sans">
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
      <router-link to="/settings" class="nav-link" active-class="nav-link-active">
        <i class="ph ph-gear icon-sm"></i> Settings
      </router-link>
    </nav>
    <main class="flex-1 p-6 overflow-y-auto">
      <router-view />
    </main>
  </div>
</template>
