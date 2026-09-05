<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import type { Player, PipelineStats } from '@/composables/usePlayer'

const props = defineProps<{
  player: Player | null
  pipelines: () => PipelineStats[]
}>()

const globalLines = ref<string[]>([])
const pipelineSections = ref<string[][]>([])
const timingLines = ref<string[]>([])
const graphRef = ref<HTMLCanvasElement | null>(null)
const samples = new Float64Array(120)
let timer = 0

function formatTs(us: number): string {
  if (us <= 0) return '--'
  const d = new Date(us / 1000)
  const pad = (n: number, w = 2) => n.toString().padStart(w, '0')
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`
}

const fmtMs = (v: number) => v.toFixed(0).padStart(4)
const fmtFps = (v: number) => v.toFixed(2).padStart(6)

function refresh() {
  const g = props.player?.globalStats?.() ?? null
  const pipes = props.pipelines()
  const count = props.player?.copyFrameTimes?.(samples) ?? 0

  globalLines.value = [
    g?.backend ?? '--',
    `${g?.mode ?? '--'}/${g?.state ?? '--'}  ${(g?.rate ?? 1).toFixed(2)}x  catchup ${(g?.catchup ?? 1).toFixed(3)}x${g?.buffering ? '  BUFFERING' : ''}`,
  ]

  pipelineSections.value = pipes.map(p => [
    `${p.label}  ${p.profile}`,
    `decode buf ${(p.bufferUs / 1000).toFixed(0).padStart(5)}ms  pos ${formatTs(p.positionUs)}`,
    `fetch  ${p.fetcherGops} GOPs  ${(p.fetcherBytes / 1048576).toFixed(1)}MiB    decode  ${p.decoderGops} GOPs  ${p.decoderFrames} fr`,
  ])

  let sum = 0
  let min = Number.MAX_VALUE
  let max = 0
  for (let i = 0; i < count; i++) {
    const v = samples[i]
    sum += v
    if (v < min) min = v
    if (v > max) max = v
  }
  if (count === 0) min = 0
  const last = count > 0 ? samples[count - 1] : 0
  const avg = count > 0 ? sum / count : 0

  const curFps = last > 0 ? 1000 / last : 0
  const avgFps = sum > 0 ? 1000 * count / sum : 0
  let sumSec = 0
  let secCount = 0
  for (let i = count - 1; i >= 0; i--) {
    sumSec += samples[i]
    secCount++
    if (sumSec >= 1000) break
  }
  const secFps = sumSec > 0 ? 1000 * secCount / sumSec : 0

  timingLines.value = [
    `dt  last ${fmtMs(last)}ms  avg ${fmtMs(avg)}ms  min ${fmtMs(min)}ms  max ${fmtMs(max)}ms`,
    `fps  cur ${fmtFps(curFps)}   1s ${fmtFps(secFps)}  avg ${fmtFps(avgFps)}`,
  ]

  drawGraph(count, max)
}

function drawGraph(count: number, max: number) {
  const canvas = graphRef.value
  if (!canvas) return
  const ctx = canvas.getContext('2d')!
  const w = canvas.width
  const h = canvas.height
  ctx.clearRect(0, 0, w, h)
  ctx.fillStyle = 'rgba(0,0,0,0.24)'
  ctx.fillRect(0, 0, w, h)
  if (count === 0) return

  const labelReserve = 6
  const barArea = h - labelReserve
  const scale = Math.max(max * 1.15, 50)
  const barW = w / samples.length
  for (let i = 0; i < count; i++) {
    const v = samples[i]
    if (v <= 0) continue
    const barH = Math.min(barArea, v / scale * barArea)
    ctx.fillStyle = v <= 45 ? 'rgba(80,200,120,0.86)'
      : v <= 80 ? 'rgba(220,190,80,0.86)'
      : 'rgba(220,90,80,0.86)'
    const x = (samples.length - count + i) * barW
    ctx.fillRect(x, h - barH, Math.max(1, barW - 0.5), barH)
  }

  ctx.fillStyle = 'rgba(255,255,255,0.35)'
  ctx.font = '9px monospace'
  ctx.fillText(`${scale.toFixed(0)}ms`, 2, 9)
}

onMounted(() => {
  timer = window.setInterval(refresh, 100)
  refresh()
})

onUnmounted(() => clearInterval(timer))
</script>

<template>
  <div class="absolute top-2 left-2 rounded bg-black/70 p-2 pointer-events-none" style="width: 340px">
    <pre class="text-[10px] leading-[13px] font-mono text-neutral-200 m-0">{{ globalLines.join('\n') }}</pre>
    <pre
      v-for="(section, i) in pipelineSections"
      :key="i"
      class="text-[10px] leading-[13px] font-mono text-neutral-200 m-0 mt-[6.5px]"
    >{{ section.join('\n') }}</pre>
    <pre class="text-[10px] leading-[13px] font-mono text-neutral-200 m-0 mt-[6.5px]">{{ timingLines.join('\n') }}</pre>
    <canvas ref="graphRef" width="324" height="64" class="mt-[6.5px] block"></canvas>
  </div>
</template>
