<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'

const darkMode = ref(false)
const sampleModalOpen = ref(false)
const sampleToggle = ref(true)
const sampleCheckbox = ref(true)
const sampleSelect = ref('option-1')
const sampleInput = ref('Sample text')
const sampleTextarea = ref('Longer form content goes here.')
const settingsExpanded = ref(true)
const motionCanvas = ref<HTMLCanvasElement | null>(null)

function drawMotionOverlay(canvas: HTMLCanvasElement) {
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const cols = 32
  const rows = 24
  canvas.width = cols
  canvas.height = rows

  const style = getComputedStyle(document.documentElement)
  const motionColor = style.getPropertyValue('--color-motion').trim()
  const activeColor = style.getPropertyValue('--color-motion-active').trim()

  ctx.clearRect(0, 0, cols, rows)

  const blobs = [
    { x: 18, y: 6, r: 2 },
    { x: 18, y: 8, r: 3 },
    { x: 18, y: 11, r: 2.5 },
    { x: 18, y: 14, r: 2 },
    { x: 17, y: 16, r: 1.5 },
    { x: 19, y: 16, r: 1.5 },
    { x: 18, y: 18, r: 1 },
    { x: 10, y: 12, r: 4 },
    { x: 10, y: 15, r: 3 },
  ]

  ctx.fillStyle = activeColor
  for (let y = 0; y < rows; y++) {
    for (let x = 0; x < cols; x++) {
      for (const b of blobs) {
        const dist = Math.sqrt((x - b.x) ** 2 + (y - b.y) ** 2)
        if (dist < b.r) {
          ctx.fillRect(x, y, 1, 1)
          break
        }
      }
    }
  }
}

onMounted(() => {
  if (motionCanvas.value) drawMotionOverlay(motionCanvas.value)
})

watch(darkMode, () => {
  requestAnimationFrame(() => {
    if (motionCanvas.value) drawMotionOverlay(motionCanvas.value)
  })
})
const animatedProgress = ref(0)
const animatedRunning = ref(false)

async function runProgress() {
  if (animatedRunning.value) return
  animatedRunning.value = true
  animatedProgress.value = 0
  const steps = [5, 10, 20, 35, 70]
  for (const step of steps) {
    await new Promise(r => setTimeout(r, 600))
    animatedProgress.value = step
  }
  const start = 70
  const end = 100
  const duration = 5000
  const interval = 250
  const totalSteps = duration / interval
  const increment = (end - start) / totalSteps
  for (let i = 1; i <= totalSteps; i++) {
    await new Promise(r => setTimeout(r, interval))
    animatedProgress.value = Math.round(start + increment * i)
  }
  animatedProgress.value = 100
  await new Promise(r => setTimeout(r, 1500))
  animatedProgress.value = 0
  animatedRunning.value = false
}

function toggleDark() {
  darkMode.value = !darkMode.value
  document.documentElement.classList.toggle('dark', darkMode.value)
}
</script>

<template>
  <div class="min-h-screen bg-surface text-text font-sans">
    <!-- Header -->
    <header class="sticky top-0 z-50 bg-surface-raised border-b border-border px-8 py-6 flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-bold text-text">Design Reference</h1>
        <p class="text-sm text-text-muted mt-1">All UI elements for the VMS web and native clients.</p>
      </div>
      <button class="btn btn-secondary" @click="toggleDark">
        <i class="icon-sm" :class="darkMode ? 'ph ph-sun' : 'ph ph-moon'"></i>
        {{ darkMode ? 'Light Mode' : 'Dark Mode' }}
      </button>
    </header>

    <main class="max-w-5xl mx-auto px-8 py-10 space-y-16">

      <!-- Colors -->
      <section class="space-y-6">
        <h2 class="section-heading">Colors</h2>

        <h3 class="section-subheading">Brand</h3>
        <div class="flex flex-wrap gap-4">
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-primary"></div>
            <span class="text-xs text-text-muted font-mono">primary</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-primary-hover"></div>
            <span class="text-xs text-text-muted font-mono">primary-hover</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-primary-muted border border-border"></div>
            <span class="text-xs text-text-muted font-mono">primary-muted</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-accent"></div>
            <span class="text-xs text-text-muted font-mono">accent</span>
          </div>
        </div>

        <h3 class="section-subheading">Surfaces</h3>
        <div class="flex flex-wrap gap-4">
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-surface border border-border"></div>
            <span class="text-xs text-text-muted font-mono">surface</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-surface-raised border border-border"></div>
            <span class="text-xs text-text-muted font-mono">surface-raised</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-surface-sunken border border-border"></div>
            <span class="text-xs text-text-muted font-mono">surface-sunken</span>
          </div>
        </div>

        <h3 class="section-subheading">Text</h3>
        <div class="flex flex-wrap gap-4">
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-text"></div>
            <span class="text-xs text-text-muted font-mono">text</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-text-muted"></div>
            <span class="text-xs text-text-muted font-mono">text-muted</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-text-inverted border border-border"></div>
            <span class="text-xs text-text-muted font-mono">text-inverted</span>
          </div>
        </div>

        <h3 class="section-subheading">Border</h3>
        <div class="flex flex-wrap gap-4">
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-border"></div>
            <span class="text-xs text-text-muted font-mono">border</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-border-muted"></div>
            <span class="text-xs text-text-muted font-mono">border-muted</span>
          </div>
        </div>

        <h3 class="section-subheading">Status</h3>
        <div class="flex flex-wrap gap-4">
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-success"></div>
            <span class="text-xs text-text-muted font-mono">success</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-warning"></div>
            <span class="text-xs text-text-muted font-mono">warning</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="w-20 h-20 rounded-lg bg-danger"></div>
            <span class="text-xs text-text-muted font-mono">danger</span>
          </div>
        </div>
      </section>

      <!-- Typography -->
      <section class="space-y-6">
        <h2 class="section-heading">Typography</h2>
        <div class="space-y-5">
          <div class="flex items-baseline gap-6">
            <span class="text-xs text-text-muted font-mono w-24 shrink-0">heading-1</span>
            <span class="text-3xl font-bold text-text">Server Dashboard</span>
          </div>
          <div class="flex items-baseline gap-6">
            <span class="text-xs text-text-muted font-mono w-24 shrink-0">heading-2</span>
            <span class="text-xl font-semibold text-text">Camera Management</span>
          </div>
          <div class="flex items-baseline gap-6">
            <span class="text-xs text-text-muted font-mono w-24 shrink-0">heading-3</span>
            <span class="text-base font-medium text-text">Stream Settings</span>
          </div>
          <div class="flex items-baseline gap-6">
            <span class="text-xs text-text-muted font-mono w-24 shrink-0">body</span>
            <span class="text-sm text-text">Camera is currently recording at 1080p resolution with H.264 codec.</span>
          </div>
          <div class="flex items-baseline gap-6">
            <span class="text-xs text-text-muted font-mono w-24 shrink-0">caption</span>
            <span class="text-xs text-text-muted">Last updated 2 minutes ago</span>
          </div>
          <div class="flex items-baseline gap-6">
            <span class="text-xs text-text-muted font-mono w-24 shrink-0">mono</span>
            <span class="text-sm font-mono text-text">192.168.1.50:554</span>
          </div>
        </div>
      </section>

      <!-- Spacing -->
      <section class="space-y-6">
        <h2 class="section-heading">Spacing Scale</h2>
        <div class="space-y-3">
          <div class="flex items-center gap-4">
            <span class="text-xs text-text-muted font-mono w-16 shrink-0 text-right">4px</span>
            <div class="h-4 w-1 bg-primary rounded-sm"></div>
            <span class="text-xs text-text-muted">1 unit</span>
          </div>
          <div class="flex items-center gap-4">
            <span class="text-xs text-text-muted font-mono w-16 shrink-0 text-right">8px</span>
            <div class="h-4 w-2 bg-primary rounded-sm"></div>
            <span class="text-xs text-text-muted">2 units</span>
          </div>
          <div class="flex items-center gap-4">
            <span class="text-xs text-text-muted font-mono w-16 shrink-0 text-right">12px</span>
            <div class="h-4 w-3 bg-primary rounded-sm"></div>
            <span class="text-xs text-text-muted">3 units</span>
          </div>
          <div class="flex items-center gap-4">
            <span class="text-xs text-text-muted font-mono w-16 shrink-0 text-right">16px</span>
            <div class="h-4 w-4 bg-primary rounded-sm"></div>
            <span class="text-xs text-text-muted">4 units (base)</span>
          </div>
          <div class="flex items-center gap-4">
            <span class="text-xs text-text-muted font-mono w-16 shrink-0 text-right">24px</span>
            <div class="h-4 w-6 bg-primary rounded-sm"></div>
            <span class="text-xs text-text-muted">6 units</span>
          </div>
          <div class="flex items-center gap-4">
            <span class="text-xs text-text-muted font-mono w-16 shrink-0 text-right">32px</span>
            <div class="h-4 w-8 bg-primary rounded-sm"></div>
            <span class="text-xs text-text-muted">8 units</span>
          </div>
          <div class="flex items-center gap-4">
            <span class="text-xs text-text-muted font-mono w-16 shrink-0 text-right">48px</span>
            <div class="h-4 w-12 bg-primary rounded-sm"></div>
            <span class="text-xs text-text-muted">12 units</span>
          </div>
        </div>
      </section>

      <!-- Buttons -->
      <section class="space-y-6">
        <h2 class="section-heading">Buttons</h2>

        <h3 class="section-subheading">With Icons</h3>
        <div class="flex flex-wrap gap-3">
          <button class="btn btn-primary"><i class="ph ph-plus icon-sm"></i> Add Camera</button>
          <button class="btn btn-secondary"><i class="ph ph-magnifying-glass icon-sm"></i> Discover</button>
          <button class="btn btn-danger"><i class="ph ph-trash icon-sm"></i> Delete</button>
          <button class="btn btn-ghost"><i class="ph ph-gear icon-sm"></i> Settings</button>
        </div>

        <h3 class="section-subheading">Without Icons</h3>
        <div class="flex flex-wrap gap-3">
          <button class="btn btn-primary">Primary</button>
          <button class="btn btn-secondary">Secondary</button>
          <button class="btn btn-danger">Danger</button>
          <button class="btn btn-ghost">Ghost</button>
        </div>

        <h3 class="section-subheading">Disabled</h3>
        <div class="flex flex-wrap gap-3">
          <button class="btn btn-primary" disabled><i class="ph ph-plus icon-sm"></i> Add Camera</button>
          <button class="btn btn-secondary" disabled><i class="ph ph-magnifying-glass icon-sm"></i> Discover</button>
          <button class="btn btn-danger" disabled><i class="ph ph-trash icon-sm"></i> Delete</button>
          <button class="btn btn-ghost" disabled><i class="ph ph-gear icon-sm"></i> Settings</button>
        </div>

        <h3 class="section-subheading">Sizes</h3>
        <div class="flex flex-wrap items-center gap-3">
          <button class="btn btn-primary btn-sm"><i class="ph ph-plus icon-sm"></i> Small</button>
          <button class="btn btn-primary"><i class="ph ph-plus icon-sm"></i> Medium</button>
          <button class="btn btn-primary btn-lg"><i class="ph ph-plus icon-md"></i> Large</button>
        </div>
      </section>

      <!-- Badges -->
      <section class="space-y-6">
        <h2 class="section-heading">Badges / Status Indicators</h2>
        <div class="flex flex-wrap gap-3">
          <span class="badge badge-success"><i class="ph-fill ph-circle icon-sm"></i> Online</span>
          <span class="badge badge-neutral"><i class="ph-fill ph-circle icon-sm"></i> Offline</span>
          <span class="badge badge-danger"><i class="ph ph-warning icon-sm"></i> Error</span>
          <span class="badge badge-warning"><i class="ph ph-warning-circle icon-sm"></i> Warning</span>
          <span class="badge badge-success"><i class="ph ph-record icon-sm"></i> Recording</span>
          <span class="badge badge-neutral"><i class="ph ph-pause icon-sm"></i> Idle</span>
        </div>
      </section>

      <!-- Form Elements -->
      <section class="space-y-6">
        <h2 class="section-heading">Form Elements</h2>

        <h3 class="section-subheading">Text Input</h3>
        <div class="grid grid-cols-3 gap-6">
          <div class="space-y-1">
            <label class="label">Default</label>
            <input type="text" class="input" v-model="sampleInput" placeholder="Camera name" />
          </div>
          <div class="space-y-1">
            <label class="label">Error</label>
            <input type="text" class="input input-error" value="bad-value" />
            <span class="text-xs text-danger"><i class="ph ph-warning-circle icon-sm"></i> Invalid camera address</span>
          </div>
          <div class="space-y-1">
            <label class="label">Disabled</label>
            <input type="text" class="input" value="Read only" disabled />
          </div>
        </div>

        <h3 class="section-subheading">Select</h3>
        <div class="max-w-xs space-y-1">
          <label class="label">Stream Profile</label>
          <select class="input" v-model="sampleSelect">
            <option value="option-1">Main (1080p)</option>
            <option value="option-2">Sub (360p)</option>
            <option value="option-3">Motion</option>
          </select>
        </div>

        <h3 class="section-subheading">Textarea</h3>
        <div class="max-w-md space-y-1">
          <label class="label">Notes</label>
          <textarea class="input" v-model="sampleTextarea" rows="3"></textarea>
        </div>

        <h3 class="section-subheading">Checkbox</h3>
        <div>
          <label class="flex items-center gap-2 text-sm text-text cursor-pointer">
            <input type="checkbox" class="accent-primary w-4 h-4" v-model="sampleCheckbox" />
            Enable recording
          </label>
        </div>

        <h3 class="section-subheading">Toggle</h3>
        <div>
          <label class="flex items-center gap-3 text-sm text-text cursor-pointer">
            Dark mode
            <button
              class="toggle-track"
              role="switch"
              :aria-checked="sampleToggle"
              @click="sampleToggle = !sampleToggle"
            >
              <span class="toggle-knob"></span>
            </button>
          </label>
        </div>
      </section>

      <!-- Cards -->
      <section class="space-y-6">
        <h2 class="section-heading">Cards</h2>

        <h3 class="section-subheading">Camera Card</h3>
        <div class="grid grid-cols-3 gap-4">
          <div class="card overflow-hidden">
            <div class="aspect-video bg-surface-sunken flex items-center justify-center">
              <i class="ph ph-video-camera icon-xl text-text-muted"></i>
            </div>
            <div class="p-3 space-y-1">
              <div class="flex items-center justify-between">
                <span class="text-sm font-medium text-text">Front Door</span>
                <span class="badge badge-success"><i class="ph-fill ph-circle icon-sm"></i> Online</span>
              </div>
              <div class="flex gap-2 text-xs text-text-muted">
                <span>1080p</span>
                <span>H.264</span>
                <span>30fps</span>
              </div>
            </div>
          </div>

          <div class="card overflow-hidden">
            <div class="aspect-video bg-surface-sunken flex items-center justify-center">
              <i class="ph ph-video-camera-slash icon-xl text-text-muted"></i>
            </div>
            <div class="p-3 space-y-1">
              <div class="flex items-center justify-between">
                <span class="text-sm font-medium text-text">Garage</span>
                <span class="badge badge-neutral"><i class="ph-fill ph-circle icon-sm"></i> Offline</span>
              </div>
              <div class="flex gap-2 text-xs text-text-muted">
                <span>1080p</span>
                <span>H.265</span>
                <span>15fps</span>
              </div>
            </div>
          </div>

          <div class="card overflow-hidden">
            <div class="aspect-video bg-surface-sunken flex items-center justify-center">
              <i class="ph ph-warning icon-xl text-danger"></i>
            </div>
            <div class="p-3 space-y-1">
              <div class="flex items-center justify-between">
                <span class="text-sm font-medium text-text">Backyard</span>
                <span class="badge badge-danger"><i class="ph ph-warning icon-sm"></i> Error</span>
              </div>
              <div class="flex gap-2 text-xs text-text-muted">
                <span>4K</span>
                <span>H.265</span>
                <span>30fps</span>
              </div>
            </div>
          </div>
        </div>

        <h3 class="section-subheading">Stat Card</h3>
        <div class="grid grid-cols-3 gap-4">
          <div class="card p-4 space-y-1">
            <div class="flex items-center gap-2">
              <i class="ph ph-video-camera icon-sm text-primary"></i>
              <span class="section-subheading">Cameras</span>
            </div>
            <span class="block text-2xl font-bold text-text">12</span>
            <span class="text-xs text-text-muted">10 online, 2 offline</span>
          </div>
          <div class="card p-4 space-y-1">
            <div class="flex items-center gap-2">
              <i class="ph ph-hard-drives icon-sm text-primary"></i>
              <span class="section-subheading">Storage</span>
            </div>
            <span class="block text-2xl font-bold text-text">1.2 TB</span>
            <span class="text-xs text-text-muted">68% used</span>
          </div>
          <div class="card p-4 space-y-1">
            <div class="flex items-center gap-2">
              <i class="ph ph-clock icon-sm text-primary"></i>
              <span class="section-subheading">Uptime</span>
            </div>
            <span class="block text-2xl font-bold text-text">14d 6h</span>
            <span class="text-xs text-text-muted">Server healthy</span>
          </div>
        </div>
      </section>

      <!-- Table -->
      <section class="space-y-6">
        <h2 class="section-heading">Table</h2>
        <div class="card overflow-hidden">
          <table class="table">
            <thead>
              <tr>
                <th>Camera</th>
                <th>Event</th>
                <th>Time</th>
                <th>Duration</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>Front Door</td>
                <td><i class="ph ph-person icon-sm text-text-muted"></i> Motion Detected</td>
                <td class="font-mono text-text-muted">2026-03-17 14:23:01</td>
                <td class="text-text-muted">12s</td>
              </tr>
              <tr>
                <td>Garage</td>
                <td><i class="ph ph-wifi-slash icon-sm text-danger"></i> Connection Lost</td>
                <td class="font-mono text-text-muted">2026-03-17 13:45:22</td>
                <td class="text-text-muted">--</td>
              </tr>
              <tr>
                <td>Backyard</td>
                <td><i class="ph ph-person icon-sm text-text-muted"></i> Motion Detected</td>
                <td class="font-mono text-text-muted">2026-03-17 12:01:55</td>
                <td class="text-text-muted">8s</td>
              </tr>
              <tr>
                <td>Driveway</td>
                <td><i class="ph ph-shield-warning icon-sm text-warning"></i> Tamper Alert</td>
                <td class="font-mono text-text-muted">2026-03-17 11:30:00</td>
                <td class="text-text-muted">--</td>
              </tr>
              <tr>
                <td>Front Door</td>
                <td><i class="ph ph-person icon-sm text-text-muted"></i> Motion Detected</td>
                <td class="font-mono text-text-muted">2026-03-17 10:15:44</td>
                <td class="text-text-muted">5s</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Navigation -->
      <section class="space-y-6">
        <h2 class="section-heading">Navigation</h2>
        <h3 class="section-subheading">Sidebar</h3>
        <div class="card flex overflow-hidden h-80">
          <nav class="nav-sidebar">
            <a href="#" class="nav-link nav-link-active"><i class="ph ph-squares-four icon-sm"></i> Gallery</a>
            <a href="#" class="nav-link"><i class="ph ph-lightning icon-sm"></i> Events</a>
            <a href="#" class="nav-link"><i class="ph ph-devices icon-sm"></i> Clients</a>
            <a href="#" class="nav-link" @click.prevent="settingsExpanded = !settingsExpanded">
              <i class="ph ph-gear icon-sm"></i> Settings
              <i class="ph ph-caret-down icon-sm nav-link-toggle" :class="{ 'nav-link-toggle-open': settingsExpanded }"></i>
            </a>
            <div v-if="settingsExpanded" class="nav-children">
              <a href="#" class="nav-child"><i class="ph ph-faders icon-sm"></i> General</a>
              <a href="#" class="nav-child nav-child-active"><i class="ph ph-hard-drives icon-sm"></i> Storage</a>
              <a href="#" class="nav-child"><i class="ph ph-clock-countdown icon-sm"></i> Retention</a>
              <a href="#" class="nav-child"><i class="ph ph-puzzle-piece icon-sm"></i> Plugins</a>
            </div>
          </nav>
          <div class="flex-1 p-6 flex items-center justify-center">
            <p class="text-text-muted text-sm">Content area</p>
          </div>
        </div>
      </section>

      <!-- Progress & Spinners -->
      <section class="space-y-6">
        <h2 class="section-heading">Progress & Spinners</h2>

        <h3 class="section-subheading">Progress Bars</h3>
        <div class="space-y-4 max-w-md">
          <div class="space-y-1">
            <div class="flex justify-between text-xs text-text-muted">
              <span>Storage Used</span>
              <span>68%</span>
            </div>
            <div class="progress-track">
              <div class="progress-fill" style="width: 68%;"></div>
            </div>
          </div>
          <div class="space-y-1">
            <div class="flex justify-between text-xs text-text-muted">
              <span>Retention Limit</span>
              <span>85%</span>
            </div>
            <div class="progress-track">
              <div class="progress-fill progress-fill-warning" style="width: 85%;"></div>
            </div>
          </div>
          <div class="space-y-1">
            <div class="flex justify-between text-xs text-text-muted">
              <span>Storage Critical</span>
              <span>96%</span>
            </div>
            <div class="progress-track">
              <div class="progress-fill progress-fill-danger" style="width: 96%;"></div>
            </div>
          </div>
          <div class="space-y-1">
            <div class="flex justify-between text-xs text-text-muted">
              <span>Upload Complete</span>
              <span>100%</span>
            </div>
            <div class="progress-track">
              <div class="progress-fill progress-fill-success" style="width: 100%;"></div>
            </div>
          </div>
          <div class="space-y-1">
            <div class="flex justify-between text-xs text-text-muted">
              <span>Animated</span>
              <span>{{ animatedProgress }}%</span>
            </div>
            <div class="progress-track">
              <div class="progress-fill" :style="{ width: animatedProgress + '%' }"></div>
            </div>
            <button class="btn btn-primary btn-sm mt-2" :disabled="animatedRunning" @click="runProgress">
              <i class="ph ph-play icon-sm"></i> Run
            </button>
          </div>
        </div>

        <h3 class="section-subheading">Spinners</h3>
        <div class="flex flex-wrap items-center gap-6">
          <div class="flex flex-col items-center gap-2">
            <div class="spinner spinner-sm"></div>
            <span class="text-xs text-text-muted">Small</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="spinner"></div>
            <span class="text-xs text-text-muted">Default</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <div class="spinner spinner-lg"></div>
            <span class="text-xs text-text-muted">Large</span>
          </div>
        </div>

        <h3 class="section-subheading">Inline Usage</h3>
        <div class="flex flex-wrap gap-3">
          <button class="btn btn-primary" disabled><div class="spinner spinner-sm"></div> Connecting...</button>
          <button class="btn btn-secondary" disabled><div class="spinner spinner-sm"></div> Discovering...</button>
        </div>
        <div class="card p-6 max-w-xs flex flex-col items-center gap-3">
          <div class="spinner spinner-lg"></div>
          <span class="text-sm text-text-muted">Loading cameras...</span>
        </div>
      </section>

      <!-- Modal -->
      <section class="space-y-6">
        <h2 class="section-heading">Modal / Dialog</h2>
        <button class="btn btn-secondary" @click="sampleModalOpen = true"><i class="ph ph-app-window icon-sm"></i> Open Sample Modal</button>
      </section>

      <!-- Toasts -->
      <section class="space-y-6">
        <h2 class="section-heading">Toasts / Notifications</h2>
        <div class="space-y-3 max-w-md">
          <div class="toast toast-success">
            <i class="ph ph-check-circle icon-xl"></i>
            <div>
              <span class="font-medium">Success</span>
              <p>Camera added successfully.</p>
            </div>
          </div>
          <div class="toast toast-danger">
            <i class="ph ph-x-circle icon-xl"></i>
            <div>
              <span class="font-medium">Error</span>
              <p>Failed to connect to camera at 192.168.1.100.</p>
            </div>
          </div>
          <div class="toast toast-info">
            <i class="ph ph-info icon-xl"></i>
            <div>
              <span class="font-medium">Info</span>
              <p>Discovery scan in progress...</p>
            </div>
          </div>
          <div class="toast toast-warning">
            <i class="ph ph-warning icon-xl"></i>
            <div>
              <span class="font-medium">Warning</span>
              <p>Storage usage above 90%.</p>
            </div>
          </div>
          <div class="toast toast-info">
            <i class="ph ph-info icon-xl"></i>
            <div>
              <span class="font-medium">Discovery Complete</span>
              <p>Found 4 cameras on the local network. 2 are already registered. 1 requires credentials. 1 is ready to add.</p>
            </div>
          </div>
          <div class="toast toast-info">
            <i class="ph ph-info icon-xl"></i>
            <div>
              <span class="font-medium">Retention Policy Applied</span>
              <p>Evaluated 12 cameras across 3 storage providers. Purged 847 segments (42.3 GB) from 6 cameras that exceeded their configured retention limits. Next evaluation in 15 minutes.</p>
            </div>
          </div>
        </div>
      </section>

      <!-- Player / Live View -->
      <section class="space-y-6">
        <h2 class="section-heading">Player / Live View</h2>
        <p class="text-sm text-text-muted">Camera detail page with live player, controls, and timeline.</p>
        <div class="card overflow-hidden">
          <!-- Player area -->
          <div class="bg-surface-sunken aspect-video relative flex items-center justify-center">
            <i class="ph ph-video-camera icon-xl text-text-muted"></i>
            <!-- Motion overlay -->
            <canvas
              ref="motionCanvas"
              class="absolute inset-0 w-full h-full pointer-events-none"
              style="image-rendering: pixelated;"
            ></canvas>
            <!-- Overlay: camera name + status -->
            <div class="absolute top-3 left-3 flex items-center gap-2">
              <span class="badge badge-success"><i class="ph-fill ph-circle icon-sm"></i> Live</span>
              <span class="text-sm font-medium video-overlay-text">Front Door</span>
            </div>
            <!-- Overlay: stream quality -->
            <div class="absolute top-3 right-3">
              <span class="badge badge-neutral">Main 1080p</span>
            </div>
            <!-- Overlay: controls -->
            <div class="absolute bottom-3 right-3 flex gap-2">
              <button class="btn btn-ghost btn-sm video-overlay-text"><i class="ph ph-corners-out icon-sm"></i></button>
              <button class="btn btn-ghost btn-sm video-overlay-text"><i class="ph ph-picture-in-picture icon-sm"></i></button>
            </div>
          </div>
          <!-- Controls bar -->
          <div class="flex items-center gap-3 px-4 py-3 border-t border-border">
            <button class="btn btn-ghost btn-sm"><i class="ph ph-pause icon-sm"></i></button>
            <button class="btn btn-ghost btn-sm"><i class="ph ph-skip-back icon-sm"></i></button>
            <button class="btn btn-ghost btn-sm"><i class="ph ph-skip-forward icon-sm"></i></button>
            <div class="flex-1 text-center">
              <span class="text-xs font-mono text-text-muted">2026-03-17 14:23:01</span>
            </div>
            <div class="flex items-center gap-2">
              <span class="section-subheading">Profile</span>
              <select class="input" style="width: auto; padding: 4px 8px; font-size: 12px;">
                <option>Main (1080p)</option>
                <option>Sub (360p)</option>
              </select>
            </div>
            <button class="btn btn-ghost btn-sm"><i class="ph ph-record icon-sm text-danger"></i></button>
            <button class="btn btn-ghost btn-sm"><i class="ph ph-person-arms-spread icon-sm"></i></button>
          </div>
          <!-- Timeline -->
          <div class="px-4 pt-2 pb-3 border-t border-border space-y-1">
            <div class="relative">
              <div class="timeline-bar">
                <div class="timeline-span timeline-span-recording" style="left: 0%; width: 35%;"></div>
                <div class="timeline-span timeline-span-recording" style="left: 40%; width: 25%;"></div>
                <div class="timeline-span timeline-span-recording" style="left: 70%; width: 30%;"></div>
                <div class="timeline-span timeline-span-motion" style="left: 12%; width: 8%;"></div>
                <div class="timeline-span timeline-span-motion" style="left: 45%; width: 12%;"></div>
                <div class="timeline-marker timeline-alert" style="left: 67%;"></div>
                <div class="timeline-marker timeline-playhead" style="left: 85%;"></div>
              </div>
              <div class="relative h-4">
                <div class="timeline-tick" style="left: 0%;">12:00</div>
                <div class="timeline-tick" style="left: 16.6%;">13:00</div>
                <div class="timeline-tick" style="left: 33.3%;">14:00</div>
                <div class="timeline-tick" style="left: 50%;">15:00</div>
                <div class="timeline-tick" style="left: 66.6%;">16:00</div>
                <div class="timeline-tick" style="left: 83.3%;">17:00</div>
                <div class="timeline-tick" style="left: 100%; transform: translateX(-100%);">18:00</div>
              </div>
            </div>
            <div class="flex items-center gap-4 text-xs text-text-muted">
              <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 timeline-span-recording rounded-sm"></span> Recording</span>
              <span class="flex items-center gap-1"><span class="inline-block w-3 h-3 timeline-span-motion rounded-sm"></span> Motion</span>
              <span class="flex items-center gap-1"><span class="inline-block w-3 h-0.5 timeline-alert"></span> Alert</span>
            </div>
          </div>
        </div>
      </section>

      <!-- Page Layout -->
      <section class="space-y-6">
        <h2 class="section-heading">Page Layout</h2>
        <p class="text-sm text-text-muted">Full page mockup showing sidebar + content area with gallery grid.</p>
        <div class="card flex overflow-hidden h-[480px]">
          <nav class="nav-sidebar">
            <div class="text-lg font-bold text-primary px-3 py-3 mb-2"><i class="ph ph-shield-check icon-md"></i> VMS</div>
            <a href="#" class="nav-link nav-link-active"><i class="ph ph-squares-four icon-sm"></i> Gallery</a>
            <a href="#" class="nav-link"><i class="ph ph-lightning icon-sm"></i> Events</a>
            <a href="#" class="nav-link"><i class="ph ph-devices icon-sm"></i> Clients</a>
            <a href="#" class="nav-link"><i class="ph ph-gear icon-sm"></i> Settings</a>
          </nav>
          <div class="flex-1 p-6 overflow-y-auto">
            <header class="flex items-center justify-between mb-6">
              <h2 class="text-xl font-semibold text-text">Gallery</h2>
              <div class="flex gap-2">
                <button class="btn btn-secondary btn-sm"><i class="ph ph-magnifying-glass icon-sm"></i> Discover</button>
                <button class="btn btn-primary btn-sm"><i class="ph ph-plus icon-sm"></i> Add Camera</button>
              </div>
            </header>
            <div class="grid grid-cols-3 gap-3">
              <div v-for="i in 6" :key="i" class="card overflow-hidden">
                <div class="aspect-video bg-surface-sunken flex items-center justify-center">
                  <i class="icon-xl" :class="i % 3 === 0 ? 'ph ph-warning text-danger' : i % 2 === 0 ? 'ph ph-video-camera-slash text-text-muted' : 'ph ph-video-camera text-text-muted'"></i>
                </div>
                <div class="p-2 flex items-center justify-between">
                  <span class="text-xs font-medium text-text">Camera {{ i }}</span>
                  <span class="badge" :class="i % 3 === 0 ? 'badge-danger' : i % 2 === 0 ? 'badge-neutral' : 'badge-success'">
                    {{ i % 3 === 0 ? 'Error' : i % 2 === 0 ? 'Offline' : 'Online' }}
                  </span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

    </main>

    <!-- Modal (outside main flow to avoid layout shift) -->
    <div v-if="sampleModalOpen" class="modal-container">
      <div class="modal-backdrop" @click="sampleModalOpen = false"></div>
      <div class="relative card p-6 w-full max-w-md shadow-modal space-y-4">
        <h3 class="text-base font-semibold text-text flex items-center gap-2">
          <i class="ph ph-warning icon-md text-danger"></i> Delete Camera
        </h3>
        <p class="text-sm text-text-muted">Are you sure you want to remove "Front Door"? Recordings will be retained according to the retention policy.</p>
        <div class="flex justify-end gap-3">
          <button class="btn btn-secondary" @click="sampleModalOpen = false">Cancel</button>
          <button class="btn btn-danger" @click="sampleModalOpen = false"><i class="ph ph-trash icon-sm"></i> Delete</button>
        </div>
      </div>
    </div>
  </div>
</template>
