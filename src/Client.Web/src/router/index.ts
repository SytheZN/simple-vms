import { createRouter, createWebHistory } from 'vue-router'
import SetupView from '@/views/SetupView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/setup', name: 'setup', component: SetupView },
    { path: '/', redirect: '/gallery' },
    { path: '/gallery', name: 'gallery', component: () => import('@/views/GalleryView.vue') },
    { path: '/gallery/:id', name: 'camera', component: () => import('@/views/CameraView.vue') },
    { path: '/events', name: 'events', component: () => import('@/views/EventsView.vue') },
    { path: '/clients', name: 'clients', component: () => import('@/views/EnrollmentView.vue') },
    { path: '/settings', redirect: '/settings/general' },
    { path: '/settings/general', name: 'settings-general', component: () => import('@/views/settings/GeneralView.vue') },
    { path: '/settings/cameras', name: 'settings-cameras', component: () => import('@/views/settings/CamerasView.vue') },
    { path: '/settings/cameras/:id', name: 'settings-camera-edit', component: () => import('@/views/settings/CameraEditView.vue') },
    { path: '/settings/storage', name: 'settings-storage', component: () => import('@/views/settings/StorageView.vue') },
    { path: '/settings/retention', name: 'settings-retention', component: () => import('@/views/settings/RetentionView.vue') },
    { path: '/settings/plugins', name: 'settings-plugins', component: () => import('@/views/settings/PluginsView.vue') },
    { path: '/settings/plugins/:id', name: 'settings-plugin-config', component: () => import('@/views/settings/PluginSettingsView.vue') },
  ],
})

export default router
