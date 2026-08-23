import type { CameraEvent, TimelineEvent } from '@/types/api'

export type EventTone = 'danger' | 'warning' | 'success' | 'info' | 'muted'

export interface EventDescriptor {
  label: string
  icon: string
  tone: EventTone
}

const descriptors: Record<string, EventDescriptor> = {
  motion: { label: 'Motion', icon: 'ph ph-person', tone: 'warning' },
  tamper: { label: 'Tamper', icon: 'ph ph-shield-warning', tone: 'danger' },
  io: { label: 'Input / Output', icon: 'ph ph-toggle-left', tone: 'info' },
  access: { label: 'Access', icon: 'ph ph-door', tone: 'info' },
  storage: { label: 'Storage', icon: 'ph ph-hard-drives', tone: 'danger' },
  generic: { label: 'Camera event', icon: 'ph ph-lightning', tone: 'info' },
  added: { label: 'Camera added', icon: 'ph ph-plus-circle', tone: 'muted' },
  config: { label: 'Reconfigured', icon: 'ph ph-gear', tone: 'muted' },
  connect: { label: 'Reconnected', icon: 'ph ph-wifi-high', tone: 'success' },
  disconnect: { label: 'Disconnected', icon: 'ph ph-wifi-slash', tone: 'danger' },
}

const fallback: EventDescriptor = {
  label: 'Unknown',
  icon: 'ph ph-question',
  tone: 'muted',
}

const toneText: Record<EventTone, string> = {
  danger: 'text-danger',
  warning: 'text-warning',
  success: 'text-success',
  info: 'text-primary',
  muted: 'text-text-muted',
}

const toneMarker: Record<EventTone, string> = {
  danger: 'timeline-event-danger',
  warning: 'timeline-event-warning',
  success: 'timeline-event-success',
  info: 'timeline-event-info',
  muted: 'timeline-event-muted',
}

export function describeEvent(type: string): EventDescriptor {
  return descriptors[type] ?? { ...fallback, label: type }
}

export const eventTypes = Object.keys(descriptors)

export function eventTextClass(type: string): string {
  return toneText[describeEvent(type).tone]
}

export function eventMarkerClass(type: string): string {
  return toneMarker[describeEvent(type).tone]
}

/**
 * ONVIF topics arrive namespace-qualified and slash-separated
 * (tns1:RuleEngine/CellMotionDetector/Motion). The prefixes identify the
 * defining schema, which tells a viewer nothing the segments do not.
 */
export function formatTopic(topic: string): string {
  return topic
    .split('/')
    .map(segment => segment.replace(/^[A-Za-z0-9._-]+:/, '').trim())
    .filter(segment => segment.length > 0)
    .join(' / ')
}

export function eventDetail(evt: CameraEvent): string | null {
  const topic = evt.metadata?.topic
  if (topic) return formatTopic(topic)

  const profile = evt.metadata?.profile
  if (profile) return profile

  return null
}

export function eventSource(evt: CameraEvent): string | null {
  const entries = Object.entries(evt.metadata ?? {})
    .filter(([key]) => key.startsWith('source.'))
    .map(([key, value]) => `${key.slice('source.'.length)}: ${value}`)

  return entries.length > 0 ? entries.join(', ') : null
}

const detailKeys = ['topic', 'profile', 'active']

/**
 * Everything not already rendered as the row's own detail line, so the
 * expanded panel adds information rather than repeating it.
 */
export function extraMetadata(evt: CameraEvent): [string, string][] {
  return Object.entries(evt.metadata ?? {})
    .filter(([key]) => !detailKeys.includes(key))
    .sort(([a], [b]) => a.localeCompare(b))
}

export function hasDetail(evt: CameraEvent): boolean {
  return Object.keys(evt.metadata ?? {}).length > 0
}

export function timelineEventTitle(evt: TimelineEvent): string {
  const when = new Date(evt.startTime / 1000).toLocaleTimeString()
  return `${describeEvent(evt.type).label} - ${when}`
}
