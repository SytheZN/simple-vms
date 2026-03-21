export type Result =
  | 'success'
  | 'created'
  | 'notFound'
  | 'badRequest'
  | 'conflict'
  | 'unauthorized'
  | 'forbidden'
  | 'internalError'
  | 'unavailable'

export interface ResponseEnvelope<T = unknown> {
  result: Result
  debugTag: number
  message?: string
  body?: T
}

export interface StreamProfile {
  profile: string
  codec: string
  resolution: string
  fps: number
  recordingEnabled: boolean
}

export interface CameraListItem {
  id: string
  name: string
  address: string
  status: string
  providerId: string
  streams: StreamProfile[]
  capabilities: string[]
}

export interface CreateCameraRequest {
  address: string
  providerId?: string
  credentials?: Credentials
  name?: string
}

export interface UpdateCameraRequest {
  name?: string
  credentials?: Credentials
  streams?: UpdateStreamConfig[]
  segmentDuration?: number
  retention?: RetentionOverride | null
}

export interface UpdateStreamConfig {
  profile: string
  recordingEnabled: boolean
}

export interface Credentials {
  username: string
  password: string
}

export interface RetentionOverride {
  mode: string
  value: number
}

export interface ClientListItem {
  id: string
  name: string
  enrolledAt: number
  lastSeenAt?: number
  connected: boolean
}

export interface UpdateClientRequest {
  name: string
}

export interface StartEnrollmentResponse {
  token: string
  qrData: string
}

export interface DiscoveryRequest {
  subnets?: string[]
  credentials?: Credentials
}

export interface DiscoveredCamera {
  address: string
  hostname?: string
  name?: string
  manufacturer?: string
  model?: string
  providerId: string
  alreadyAdded: boolean
}

export interface RecordingSegment {
  id: string
  startTime: number
  endTime: number
  profile: string
  sizeBytes: number
}

export interface TimelineResponse {
  spans: TimelineSpan[]
  events: TimelineEvent[]
}

export interface TimelineSpan {
  startTime: number
  endTime: number
}

export interface TimelineEvent {
  id: string
  type: string
  startTime: number
  endTime?: number
}

export interface CameraEvent {
  id: string
  cameraId: string
  type: string
  startTime: number
  endTime?: number
  metadata?: Record<string, string>
}

export interface RetentionPolicy {
  mode: string
  value: number
}

export interface HealthResponse {
  status: string
  uptime: number
  version: string
}

export interface StorageResponse {
  stores: StorageStore[]
}

export interface StorageStore {
  totalBytes: number
  usedBytes: number
  freeBytes: number
  recordingBytes: number
}

export interface ServerSettings {
  serverName?: string
  externalEndpoint?: string
  segmentDuration?: number
  discoverySubnets?: string[]
  defaultCredentials?: Credentials
}

export interface PluginListItem {
  id: string
  name: string
  description?: string
  version: string
  status: string
  extensionPoints: string[]
  userStartable: boolean
}

export interface SettingGroup {
  key: string
  order: number
  label: string
  description?: string
  fields: SettingField[]
}

export interface SettingField {
  key: string
  order: number
  label: string
  type: string
  description?: string
  defaultValue?: unknown
  required: boolean
  value?: unknown
}

export interface StreamMetadata {
  mimeType: string
  resolution: string
}
