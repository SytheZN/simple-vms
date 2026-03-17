import { createRouter, createWebHistory } from 'vue-router'
import SetupView from '@/views/SetupView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/setup', name: 'setup', component: SetupView },
    { path: '/', name: 'gallery', component: () => import('@/views/GalleryView.vue') },
    { path: '/events', name: 'events', component: () => import('@/views/EventsView.vue') },
    { path: '/clients', name: 'clients', component: () => import('@/views/EnrollmentView.vue') },
    { path: '/settings', name: 'settings', component: () => import('@/views/SettingsView.vue') },
  ],
})

export default router
