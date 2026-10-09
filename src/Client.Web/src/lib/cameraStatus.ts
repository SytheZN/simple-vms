import type { CameraListItem } from '@/types/api'

export function formatPauseEnd(pausedUntilUs: number): string {
  return new Date(pausedUntilUs / 1000).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

export function cameraStatusLabel(camera: CameraListItem): string {
  if (camera.status === 'paused' && camera.pausedUntil)
    return `paused until ${formatPauseEnd(camera.pausedUntil)}`
  return camera.status
}
