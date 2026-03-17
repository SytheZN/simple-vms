import type {
  ResponseEnvelope,
  CameraListItem,
  CreateCameraRequest,
  UpdateCameraRequest,
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
  StorageResponse,
  ServerSettings,
  PluginListItem,
} from '@/types/api'

class ApiError extends Error {
  result: string
  debugTag: number

  constructor(result: string, debugTag: number, message: string) {
    super(message)
    this.result = result
    this.debugTag = debugTag
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const init: RequestInit = {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  }

  const res = await fetch(path, init)
  if (res.status === 204) return undefined as T

  const envelope: ResponseEnvelope<T> = await res.json()
  if (envelope.result !== 'Success' && envelope.result !== 'Created') {
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
    update: (id: string, body: UpdateCameraRequest) => put<void>(`/api/v1/cameras/${id}`, body),
    delete: (id: string) => del<void>(`/api/v1/cameras/${id}`),
    restart: (id: string) => post<void>(`/api/v1/cameras/${id}/restart`),
    snapshot: (id: string) => `/api/v1/cameras/${id}/snapshot`,
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
    generateCerts: () => post<void>('/api/v1/system/certs'),
  },

  plugins: {
    list: () => get<PluginListItem[]>('/api/v1/plugins'),
    get: (id: string) => get<PluginListItem>(`/api/v1/plugins/${id}`),
    updateConfig: (id: string, body: unknown) => put<void>(`/api/v1/plugins/${id}/config`, body),
    start: (id: string) => post<void>(`/api/v1/plugins/${id}/start`),
    stop: (id: string) => post<void>(`/api/v1/plugins/${id}/stop`),
  },
}

export { ApiError }
