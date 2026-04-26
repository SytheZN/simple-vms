import type {
  ResponseEnvelope,
  CameraListItem,
  CreateCameraRequest,
  UpdateCameraRequest,
  ProbeRequest,
  ProbeResponse,
  ClientListItem,
  UpdateClientRequest,
  StartEnrollmentResponse,
  DiscoveryRequest,
  DiscoveredCamera,
  RecordingSegment,
  TimelineResponse,
  CameraEvent,
  RetentionPolicy,
  HealthResponse,
  VerifyRemoteAddressResponse,
  StorageResponse,
  ServerSettings,
  PluginListItem,
  SettingGroup,
  CameraConfigSchema,
  CameraConfigValues,
} from '@/types/api'

import router from '@/router'

class ApiError extends Error {
  result: string
  debugTag: string

  constructor(result: string, debugTag: string, message: string) {
    super(message)
    this.result = result
    this.debugTag = debugTag
  }
}

type ConfigurationReason =
  | 'missing-certs'
  | 'starting'
  | 'data-provider-unavailable'
  | 'missing-settings'

class ConfigurationRequiredError extends Error {
  reason: ConfigurationReason
  missing?: string[]
  constructor(reason: ConfigurationReason, missing?: string[]) {
    super(`configuration-required: ${reason}`)
    this.reason = reason
    this.missing = missing
  }
}

async function handleConfigurationRequired(res: Response): Promise<ConfigurationRequiredError | null> {
  let body: { error?: string; reason?: string; missing?: string[] } | null = null
  try {
    body = await res.clone().json()
  } catch {
    return null
  }
  if (body?.error !== 'configuration-required' || !body.reason) return null

  const reason = body.reason as ConfigurationReason
  const currentName = router.currentRoute.value.name
  switch (reason) {
    case 'missing-certs':
    case 'data-provider-unavailable':
      if (currentName !== 'setup') await router.replace('/setup')
      break
    case 'missing-settings':
      if (currentName !== 'setup-complete') await router.replace('/setup/complete')
      break
    case 'starting':
      break
  }
  return new ConfigurationRequiredError(reason, body.missing)
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const init: RequestInit = {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  }

  const res = await fetch(path, init)
  if (res.status === 204) return undefined as T

  if (res.status === 412) {
    const configError = await handleConfigurationRequired(res)
    if (configError) throw configError
    throw new ApiError('preconditionFailed', '', 'Precondition failed')
  }

  const envelope: ResponseEnvelope<T> = await res.json()
  if (envelope.result !== 'success' && envelope.result !== 'created') {
    throw new ApiError(envelope.result, envelope.debugTag, envelope.message ?? envelope.result)
  }
  return envelope.body as T
}

function get<T>(path: string): Promise<T> {
  return request<T>('GET', path)
}

function post<T>(path: string, body?: unknown): Promise<T> {
  return request<T>('POST', path, body)
}

function put<T>(path: string, body?: unknown): Promise<T> {
  return request<T>('PUT', path, body)
}

function del<T>(path: string): Promise<T> {
  return request<T>('DELETE', path)
}

function qs(params: Record<string, string | number | undefined>): string {
  const entries = Object.entries(params).filter(([, v]) => v !== undefined)
  if (entries.length === 0) return ''
  return '?' + new URLSearchParams(entries.map(([k, v]) => [k, String(v)])).toString()
}

export const api = {
  enrollment: {
    start: () => post<StartEnrollmentResponse>('/api/v1/clients/enroll'),
    hold: (token: string, signal: AbortSignal) =>
      fetch(`/api/v1/clients/enroll/${token}/hold`, { signal }).catch(() => {}),
  },

  clients: {
    list: () => get<ClientListItem[]>('/api/v1/clients'),
    get: (id: string) => get<ClientListItem>(`/api/v1/clients/${id}`),
    update: (id: string, body: UpdateClientRequest) => put<void>(`/api/v1/clients/${id}`, body),
    revoke: (id: string) => del<void>(`/api/v1/clients/${id}`),
  },

  cameras: {
    list: (status?: string) => get<CameraListItem[]>(`/api/v1/cameras${qs({ status })}`),
    get: (id: string) => get<CameraListItem>(`/api/v1/cameras/${id}`),
    create: (body: CreateCameraRequest) => post<CameraListItem>('/api/v1/cameras', body),
    probe: (body: ProbeRequest) => post<ProbeResponse>('/api/v1/cameras/probe', body),
    update: (id: string, body: UpdateCameraRequest) => put<void>(`/api/v1/cameras/${id}`, body),
    delete: (id: string) => del<void>(`/api/v1/cameras/${id}`),
    refresh: (id: string) => post<CameraListItem>(`/api/v1/cameras/${id}/refresh`),
    restart: (id: string) => post<void>(`/api/v1/cameras/${id}/restart`),
    snapshot: (id: string) => `/api/v1/cameras/${id}/snapshot`,
    configSchema: (id: string) => request<CameraConfigSchema>('OPTIONS', `/api/v1/cameras/${id}/config`),
    configValues: (id: string) => get<CameraConfigValues>(`/api/v1/cameras/${id}/config`),
    updateConfig: (id: string, body: CameraConfigValues) => put<void>(`/api/v1/cameras/${id}/config`, body),
  },

  discovery: {
    run: (body: DiscoveryRequest) => post<DiscoveredCamera[]>('/api/v1/discovery', body),
  },

  recordings: {
    list: (cameraId: string, from: number, to: number, profile?: string) =>
      get<RecordingSegment[]>(`/api/v1/recordings/${cameraId}${qs({ from, to, profile })}`),
    timeline: (cameraId: string, from: number, to: number, profile?: string) =>
      get<TimelineResponse>(`/api/v1/recordings/${cameraId}/timeline${qs({ from, to, profile })}`),
  },

  events: {
    list: (params: {
      cameraId?: string
      type?: string
      from: number
      to: number
      limit?: number
      offset?: number
    }) => get<CameraEvent[]>(`/api/v1/events${qs(params)}`),
    get: (id: string) => get<CameraEvent>(`/api/v1/events/${id}`),
  },

  retention: {
    get: () => get<RetentionPolicy>('/api/v1/retention'),
    update: (body: RetentionPolicy) => put<void>('/api/v1/retention', body),
  },

  system: {
    health: () => get<HealthResponse>('/api/v1/system/health'),
    storage: () => get<StorageResponse>('/api/v1/system/storage'),
    settings: () => get<ServerSettings>('/api/v1/system/settings'),
    updateSettings: (body: ServerSettings) => put<void>('/api/v1/system/settings', body),
    verifyRemoteAddress: (host?: string) =>
      get<VerifyRemoteAddressResponse>(
        `/api/v1/system/verify-remote-address${qs({ host })}`),
    generateCerts: () => post<void>('/api/v1/system/certs'),
  },

  plugins: {
    list: (type?: string) => get<PluginListItem[]>(`/api/v1/plugins${qs({ type })}`),
    get: (id: string) => get<PluginListItem>(`/api/v1/plugins/${id}`),
    configSchema: (id: string) => request<SettingGroup[]>('OPTIONS', `/api/v1/plugins/${id}/config`),
    configValues: (id: string) => get<Record<string, unknown>>(`/api/v1/plugins/${id}/config`),
    updateConfig: (id: string, body: Record<string, unknown>) => put<void>(`/api/v1/plugins/${id}/config`, body),
    validateField: (id: string, key: string, value: unknown) => post<void>(`/api/v1/plugins/${id}/config/validate`, { key, value }),
    start: (id: string) => post<void>(`/api/v1/plugins/${id}/start`),
    stop: (id: string) => post<void>(`/api/v1/plugins/${id}/stop`),
  },

}

export { ApiError, ConfigurationRequiredError }
export type { ConfigurationReason }
