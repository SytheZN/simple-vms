<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import QRCode from 'qrcode'
import { api, ApiError } from '@/api/client'
import type { ClientListItem } from '@/types/api'

const clients = ref<ClientListItem[]>([])
const token = ref('')
const qrDataUrl = ref('')
const enrolling = ref(false)
const error = ref('')
const editingId = ref<string | null>(null)
const editName = ref('')

let heartbeat: ReturnType<typeof setInterval> | undefined

async function loadClients() {
  try {
    clients.value = await api.clients.list()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function startEnrollment() {
  enrolling.value = true
  error.value = ''
  token.value = ''
  qrDataUrl.value = ''
  try {
    const res = await api.enrollment.start()
    token.value = res.token
    qrDataUrl.value = await QRCode.toDataURL(res.qrData, {
      width: 256,
      margin: 2,
      color: { dark: '#000000', light: '#ffffff' }
    })
    heartbeat = setInterval(loadClients, 3000)
  } catch (e) {
    enrolling.value = false
    if (e instanceof ApiError) error.value = e.message
  }
}

function cancelEnrollment() {
  enrolling.value = false
  token.value = ''
  qrDataUrl.value = ''
  clearInterval(heartbeat)
}

async function startEdit(client: ClientListItem) {
  editingId.value = client.id
  editName.value = client.name
}

async function saveEdit(id: string) {
  try {
    await api.clients.update(id, { name: editName.value })
    editingId.value = null
    await loadClients()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

async function revokeClient(id: string) {
  try {
    await api.clients.revoke(id)
    await loadClients()
  } catch (e) {
    if (e instanceof ApiError) error.value = e.message
  }
}

function formatTime(micros: number): string {
  return new Date(micros / 1000).toLocaleString()
}

watch(enrolling, (val) => {
  if (!val) clearInterval(heartbeat)
})

onMounted(loadClients)
onUnmounted(() => clearInterval(heartbeat))
</script>

<template>
  <div class="space-y-8">
    <h1 class="section-heading">Clients</h1>
    <div>
      <button v-if="!enrolling" class="btn btn-primary" @click="startEnrollment">
        <i class="ph ph-plus icon-sm"></i> Add Client
      </button>
    </div>

    <div v-if="error" class="toast toast-danger">
      <i class="ph ph-x-circle icon-xl"></i>
      <div>
        <span class="font-medium">Error</span>
        <p>{{ error }}</p>
      </div>
    </div>

    <div v-if="enrolling" class="card p-6 space-y-4 max-w-md">
      <h2 class="text-lg font-semibold text-text flex items-center gap-2">
        <i class="ph ph-qr-code icon-md text-primary"></i> Enroll Client
      </h2>
      <p class="text-sm text-text-muted">Scan the QR code with a mobile client, or enter the token manually on a desktop client.</p>

      <div v-if="qrDataUrl" class="flex justify-center">
        <img :src="qrDataUrl" alt="Enrollment QR code" class="rounded-lg" />
      </div>

      <div class="flex items-center justify-center gap-2 text-2xl font-bold font-mono text-text tracking-widest">
        {{ token }}
      </div>

      <p class="text-xs text-text-muted text-center">Token is valid while this panel is open.</p>

      <button class="btn btn-secondary w-full" @click="cancelEnrollment">
        <i class="ph ph-x icon-sm"></i> Cancel
      </button>
    </div>

    <div class="card overflow-hidden">
      <table class="table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Status</th>
            <th>Enrolled</th>
            <th>Last Seen</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="clients.length === 0">
            <td colspan="5" class="text-center text-text-muted">No clients enrolled.</td>
          </tr>
          <tr v-for="client in clients" :key="client.id">
            <td>
              <div v-if="editingId === client.id" class="flex items-center gap-2">
                <input class="input" v-model="editName" @keyup.enter="saveEdit(client.id)" />
                <button class="btn btn-primary btn-sm" @click="saveEdit(client.id)">
                  <i class="ph ph-check icon-sm"></i>
                </button>
                <button class="btn btn-ghost btn-sm" @click="editingId = null">
                  <i class="ph ph-x icon-sm"></i>
                </button>
              </div>
              <span v-else>{{ client.name }}</span>
            </td>
            <td>
              <span class="badge" :class="client.connected ? 'badge-success' : 'badge-neutral'">
                <i class="ph-fill ph-circle icon-sm"></i>
                {{ client.connected ? 'Connected' : 'Disconnected' }}
              </span>
            </td>
            <td class="font-mono text-text-muted">{{ formatTime(client.enrolledAt) }}</td>
            <td class="font-mono text-text-muted">{{ client.lastSeenAt ? formatTime(client.lastSeenAt) : '--' }}</td>
            <td>
              <div class="flex items-center gap-1 justify-end">
                <button class="btn btn-ghost btn-sm" @click="startEdit(client)" title="Rename">
                  <i class="ph ph-pencil icon-sm"></i>
                </button>
                <button class="btn btn-ghost btn-sm text-danger" @click="revokeClient(client.id)" title="Revoke">
                  <i class="ph ph-trash icon-sm"></i>
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
