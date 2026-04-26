<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { api, ApiError } from '@/api/client'
import type { RemoteAccessMode, ServerSettings } from '@/types/api'
import {
  validateHostOrIp,
  validateExternalPort,
  validateRouterAddress,
  guessRouterAddress,
  isIpLiteral,
  EXTERNAL_PORT_MIN_UPNP,
  EXTERNAL_PORT_MAX_UPNP
} from '@/lib/validation/networkEndpoints'

const props = defineProps<{
  modelValue: ServerSettings
  tunnelPortHint?: number
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: ServerSettings): void
  (e: 'validity', valid: boolean): void
}>()

const settings = computed({
  get: () => props.modelValue,
  set: v => emit('update:modelValue', v)
})

function update<K extends keyof ServerSettings>(key: K, value: ServerSettings[K]) {
  emit('update:modelValue', { ...settings.value, [key]: value })
}

const mode = computed<RemoteAccessMode>(() => settings.value.mode ?? 'none')

function setMode(next: RemoteAccessMode) {
  const base = { ...settings.value, mode: next }
  if (next === 'none') {
    base.externalHost = undefined
    base.externalPort = undefined
    base.upnpRouterAddress = undefined
  } else if (next === 'manual') {
    base.upnpRouterAddress = undefined
  }
  emit('update:modelValue', base)
}

watch(
  () => mode.value,
  (currentMode) => {
    if (currentMode !== 'upnp' || settings.value.upnpRouterAddress) return
    const guess = guessRouterAddress(window.location.hostname)
    if (guess) update('upnpRouterAddress', guess)
  },
  { immediate: true }
)

function generatePort() {
  const range = EXTERNAL_PORT_MAX_UPNP - EXTERNAL_PORT_MIN_UPNP + 1
  update('externalPort', EXTERNAL_PORT_MIN_UPNP + Math.floor(Math.random() * range))
}

const hostError = computed(() => {
  if (mode.value === 'none') return ''
  const h = (settings.value.externalHost ?? '').trim()
  if (!h) return ''
  const v = validateHostOrIp(h, { allowPort: false, fieldLabel: 'External host' })
  return v.valid ? '' : v.error ?? ''
})

const portError = computed(() => {
  if (mode.value === 'none') return ''
  if (settings.value.externalPort === undefined || settings.value.externalPort === null) return ''
  const v = validateExternalPort(settings.value.externalPort, mode.value)
  return v.valid ? '' : v.error ?? ''
})

const routerError = computed(() => {
  if (mode.value !== 'upnp') return ''
  const r = (settings.value.upnpRouterAddress ?? '').trim()
  if (!r) return ''
  const v = validateRouterAddress(r)
  return v.valid ? '' : v.error ?? ''
})

const sectionValid = computed(() => {
  if (hostError.value || portError.value || routerError.value) return false
  if (mode.value === 'none') return true
  if (!(settings.value.externalHost ?? '').trim()) return false
  if (settings.value.externalPort === undefined || settings.value.externalPort === null) return false
  if (mode.value === 'upnp' && !(settings.value.upnpRouterAddress ?? '').trim()) return false
  return true
})
watch(sectionValid, v => emit('validity', v), { immediate: true })

type VerifyState =
  | { kind: 'match'; publicIp: string; resolvedIps?: string[] }
  | { kind: 'mismatch'; publicIp: string; resolvedIps?: string[] }
  | { kind: 'info'; publicIp: string }
  | { kind: 'error'; message: string }

const verifying = ref(false)
const verifyResult = ref<VerifyState | null>(null)

async function verifyExternal() {
  verifyResult.value = null
  verifying.value = true
  try {
    const host = (settings.value.externalHost ?? '').trim()
    const result = await api.system.verifyRemoteAddress(host || undefined)
    const publicIp = result.publicIp

    if (!host) {
      verifyResult.value = { kind: 'info', publicIp }
      return
    }

    if (isIpLiteral(host)) {
      verifyResult.value = {
        kind: host === publicIp ? 'match' : 'mismatch',
        publicIp,
        resolvedIps: [host]
      }
      return
    }

    const resolved = result.resolvedIps ?? []
    verifyResult.value = {
      kind: resolved.includes(publicIp) ? 'match' : 'mismatch',
      publicIp,
      resolvedIps: resolved
    }
  } catch (e) {
    const reason = e instanceof ApiError ? e.message : 'unknown error'
    verifyResult.value = { kind: 'error', message: reason }
  } finally {
    verifying.value = false
  }
}

const verifyToastClass = computed(() => {
  switch (verifyResult.value?.kind) {
    case 'match': return 'toast toast-success'
    case 'mismatch': return 'toast toast-danger'
    case 'error': return 'toast toast-danger'
    case 'info': return 'toast toast-info'
    default: return ''
  }
})

const verifyIconClass = computed(() => {
  switch (verifyResult.value?.kind) {
    case 'match': return 'ph-check-circle'
    case 'mismatch':
    case 'error': return 'ph-x-circle'
    case 'info': return 'ph-info'
    default: return ''
  }
})

const verifyTitle = computed(() => {
  switch (verifyResult.value?.kind) {
    case 'match': return 'Hostname resolves to your public IP'
    case 'mismatch': return 'Hostname does not match your public IP'
    case 'info': return 'Public IP detected'
    case 'error': return 'Lookup failed'
    default: return ''
  }
})

const internalIpHint = computed(() => {
  const ie = (settings.value.internalEndpoint ?? '').trim()
  if (!ie) return "your server's LAN IP"
  const bracketClose = ie.lastIndexOf(']')
  const colon = ie.lastIndexOf(':')
  const host = colon > bracketClose ? ie.slice(0, colon) : ie
  return host
})

const externalHostIsIp = computed(() => isIpLiteral(settings.value.externalHost))

const portForwardingToastClass = computed(() => {
  const status = settings.value.portForwardingStatus
  if (!status) return ''
  if (status.active && !status.lastError) return 'toast toast-success'
  if (status.lastError) return 'toast toast-danger'
  return 'toast toast-info'
})

const portForwardingIconClass = computed(() => {
  const status = settings.value.portForwardingStatus
  if (!status) return ''
  if (status.active && !status.lastError) return 'ph-check-circle'
  if (status.lastError) return 'ph-x-circle'
  return 'ph-info'
})

const portForwardingTitle = computed(() => {
  const status = settings.value.portForwardingStatus
  if (!status) return ''
  if (status.active && !status.lastError) return 'Port forwarding active'
  if (status.active && status.lastError) return 'Port forwarding refresh failed'
  if (status.lastError) return 'Port forwarding failed'
  return 'Port forwarding not yet applied'
})
</script>

<template>
  <section class="space-y-4">
    <h2 class="section-subheading">Remote access</h2>
    <div class="card p-6 space-y-4">
      <div class="space-y-2">
        <label class="label">Mode</label>
        <div class="flex flex-col gap-2">
          <label class="flex items-start gap-2 cursor-pointer">
            <input
              type="radio"
              name="remote-access-mode"
              class="mt-1"
              :checked="mode === 'none'"
              @change="setMode('none')"
            />
            <div>
              <span class="font-medium text-text">None</span>
              <p class="text-xs text-text-muted">Server is reachable on your LAN only.</p>
            </div>
          </label>
          <label class="flex items-start gap-2 cursor-pointer">
            <input
              type="radio"
              name="remote-access-mode"
              class="mt-1"
              :checked="mode === 'manual'"
              @change="setMode('manual')"
            />
            <div>
              <span class="font-medium text-text">Manual</span>
              <p class="text-xs text-text-muted">You'll configure port forwarding on your router yourself.</p>
            </div>
          </label>
          <label class="flex items-start gap-2 cursor-pointer">
            <input
              type="radio"
              name="remote-access-mode"
              class="mt-1"
              :checked="mode === 'upnp'"
              @change="setMode('upnp')"
            />
            <div>
              <span class="font-medium text-text">Automatic</span>
              <p class="text-xs text-text-muted">Ask the router to forward a port via NAT-PMP, falling back to UPnP. Requires one of them enabled on the router.</p>
            </div>
          </label>
        </div>
      </div>

      <template v-if="mode !== 'none'">
        <div class="border-t border-border pt-4 space-y-4">
          <div class="space-y-1">
            <label class="label">External host</label>
            <p class="text-xs text-text-muted">
              The public hostname or IP address clients outside your network will dial.
              Use a Dynamic DNS hostname if your ISP changes your public IP.
            </p>
            <div class="flex gap-2">
              <input
                class="input"
                :value="settings.externalHost ?? ''"
                @input="update('externalHost', ($event.target as HTMLInputElement).value)"
                placeholder="myhome.ddns.net or 203.0.113.42"
              />
              <button
                type="button"
                class="btn btn-secondary btn-sm whitespace-nowrap"
                :disabled="verifying"
                @click="verifyExternal"
              >
                <div v-if="verifying" class="spinner spinner-sm"></div>
                <i v-else class="ph ph-magnifying-glass icon-sm"></i>
                Verify
              </button>
            </div>
            <p v-if="hostError" class="text-xs text-danger">{{ hostError }}</p>
            <p v-else class="text-xs text-text-muted text-right">
              Verify contacts api.ipify.org from the server.
            </p>
            <div v-if="verifyResult" :class="verifyToastClass">
              <i class="ph icon-xl" :class="verifyIconClass"></i>
              <div class="space-y-1 text-sm">
                <span class="font-medium">{{ verifyTitle }}</span>
                <template v-if="verifyResult.kind === 'error'">
                  <p>{{ verifyResult.message }}</p>
                </template>
                <dl v-else class="grid grid-cols-[auto_1fr] gap-x-3">
                  <dt>Public IP:</dt>
                  <dd class="font-mono">{{ verifyResult.publicIp }}</dd>
                  <template v-if="verifyResult.kind !== 'info' && verifyResult.resolvedIps?.length">
                    <dt>Resolves to:</dt>
                    <dd class="font-mono">{{ verifyResult.resolvedIps.join(', ') }}</dd>
                  </template>
                </dl>
              </div>
            </div>
          </div>

          <div class="space-y-1">
            <label class="label">External port</label>
            <div class="flex gap-2">
              <input
                class="input"
                type="number"
                :value="settings.externalPort ?? ''"
                @input="update('externalPort', Number(($event.target as HTMLInputElement).value) || undefined)"
                :min="mode === 'upnp' ? EXTERNAL_PORT_MIN_UPNP : 1"
                :max="mode === 'upnp' ? EXTERNAL_PORT_MAX_UPNP : 65535"
                placeholder="e.g. 34567"
              />
              <button
                v-if="mode === 'upnp'"
                type="button"
                class="btn btn-secondary btn-sm whitespace-nowrap"
                @click="generatePort"
              >
                <i class="ph ph-shuffle icon-sm"></i> Generate
              </button>
            </div>
            <p v-if="portError" class="text-xs text-danger">{{ portError }}</p>
            <p v-else-if="mode === 'upnp'" class="text-xs text-text-muted">
              Allowed range: {{ EXTERNAL_PORT_MIN_UPNP }}-{{ EXTERNAL_PORT_MAX_UPNP }}.
            </p>
            <p v-else class="text-xs text-text-muted">
              The port clients will dial. Doesn't need to match the tunnel port.
            </p>
          </div>

          <template v-if="mode === 'upnp'">
            <div class="space-y-1">
              <label class="label">Router address</label>
              <input
                class="input"
                :value="settings.upnpRouterAddress ?? ''"
                @input="update('upnpRouterAddress', ($event.target as HTMLInputElement).value)"
                placeholder="192.168.1.1"
              />
              <p v-if="routerError" class="text-xs text-danger">{{ routerError }}</p>
              <p v-else class="text-xs text-text-muted">
                Your router's LAN IP.
              </p>
            </div>
          </template>

          <div v-if="mode === 'manual'" class="toast toast-info">
            <i class="ph ph-info icon-xl"></i>
            <div class="space-y-1">
              <span class="font-medium">Configure your router</span>
              <ol class="list-decimal list-inside space-y-1">
                <li>
                  Forward TCP port
                  <span class="font-mono">{{ settings.externalPort ?? '?' }}</span>
                  on your router to
                  <span class="font-mono">{{ internalIpHint }}</span>:<span class="font-mono">{{ tunnelPortHint ?? '?' }}</span>.
                </li>
                <li v-if="externalHostIsIp">
                  <span class="font-mono">{{ settings.externalHost }}</span>
                  must be your router's public IP. If your ISP rotates it (most do), switch to a
                  Dynamic DNS hostname (DuckDNS, No-IP) instead of a bare IP.
                </li>
                <li v-else>Click Verify to confirm the hostname resolves to your public IP.</li>
                <li>
                  Once configured, clients outside your network will reach this server at
                  <span class="font-mono">{{ settings.externalHost || '?' }}:{{ settings.externalPort ?? '?' }}</span>.
                </li>
              </ol>
            </div>
          </div>

          <div
            v-if="mode === 'upnp' && settings.portForwardingStatus"
            :class="portForwardingToastClass"
          >
            <i class="ph icon-xl" :class="portForwardingIconClass"></i>
            <div class="space-y-1 text-sm">
              <span class="font-medium">{{ portForwardingTitle }}</span>
              <dl
                v-if="settings.portForwardingStatus.active"
                class="grid grid-cols-[auto_1fr] gap-x-3"
              >
                <dt>Protocol:</dt>
                <dd class="font-mono">{{ settings.portForwardingStatus.protocol }}</dd>
                <dt>Mapping:</dt>
                <dd class="font-mono">
                  external {{ settings.portForwardingStatus.externalPort }}
                  -&gt; {{ settings.portForwardingStatus.internalPort }}
                </dd>
              </dl>
              <p v-if="settings.portForwardingStatus.lastError">
                {{ settings.portForwardingStatus.lastError }}
              </p>
            </div>
          </div>
        </div>
      </template>

      <div class="toast toast-info">
        <i class="ph ph-warning icon-xl"></i>
        <div>
          <span class="font-medium">Warning</span>
          <p>These values are baked into every enrolled client. Changes here require re-enrolling clients.</p>
        </div>
      </div>
    </div>
  </section>
</template>
