import type { Fetcher } from './fetcher'

export interface DecodedItem {
  timestamp: number
}

export interface ChunkDecoder<F extends DecodedItem> {
  onFrame: ((frame: F, gopTimestamp?: number) => void) | null
  decode(data: Uint8Array, gopTimestamp: number): void
  dispose(frame: F): void
}

interface DecodedGop<F extends DecodedItem> {
  timestamp: number
  frames: F[]
}

export class DecodeController<F extends DecodedItem> {
  private readonly fetcher: Fetcher
  private readonly codec: ChunkDecoder<F>
  private gops: DecodedGop<F>[] = []
  private currentGop: DecodedGop<F> | null = null
  private decodedChunks = new Map<number, number>()

  constructor(fetcher: Fetcher, codec: ChunkDecoder<F>) {
    this.fetcher = fetcher
    this.codec = codec
    codec.onFrame = (frame, gopTimestamp) => this.pushFrame(frame, gopTimestamp)
  }

  setTarget(gopTimestamps: number[]) {
    const targetSet = new Set(gopTimestamps)
    const maxKeep = gopTimestamps.length + 2
    if (this.gops.length > maxKeep) {
      const toRemove = this.gops
        .filter(g => !targetSet.has(g.timestamp))
        .slice(0, this.gops.length - maxKeep)
      for (const gop of toRemove) {
        for (const f of gop.frames) this.codec.dispose(f)
        this.gops.splice(this.gops.indexOf(gop), 1)
        this.decodedChunks.delete(gop.timestamp)
      }
    }

    for (const gopTs of gopTimestamps) {
      const gop = this.fetcher.findGop(gopTs)
      if (!gop || gop.timestamp !== gopTs) continue

      const decoded = this.decodedChunks.get(gopTs) ?? 0
      if (decoded > gop.chunks.length) {
        this.decodedChunks.set(gopTs, gop.chunks.length)
        continue
      }
      if (decoded === gop.chunks.length) continue

      for (let i = decoded; i < gop.chunks.length; i++)
        this.codec.decode(gop.chunks[i], gopTs)
      this.decodedChunks.set(gopTs, gop.chunks.length)
    }
  }

  hasFrameToward(ts: number, direction: 1 | -1): boolean {
    const covers = (f: F) => f.timestamp !== 0
      && (direction === 1 ? f.timestamp >= ts : f.timestamp <= ts)
    for (const gop of this.gops)
      for (const f of gop.frames)
        if (covers(f)) return true
    if (this.currentGop)
      for (const f of this.currentGop.frames)
        if (covers(f)) return true
    return false
  }

  getFrame(ts: number): F | null {
    let best: F | null = null
    let bestDist = Infinity

    const search = (gop: DecodedGop<F>) => {
      for (const f of gop.frames) {
        if (f.timestamp === 0) continue
        const dist = Math.abs(f.timestamp - ts)
        if (dist < bestDist) {
          bestDist = dist
          best = f
        }
      }
    }

    for (const gop of this.gops) search(gop)
    if (this.currentGop) search(this.currentGop)
    return best
  }

  beginGop(gopTimestamp: number): boolean {
    if (this.currentGop?.timestamp === gopTimestamp) return false
    this.finalizeCurrent()
    this.currentGop = { timestamp: gopTimestamp, frames: [] }
    return true
  }

  restartGop(gopTimestamp: number) {
    this.finalizeCurrent()
    this.currentGop = { timestamp: gopTimestamp, frames: [] }
  }

  finalizeCurrent() {
    if (!this.currentGop || this.currentGop.frames.length === 0) return
    const idx = this.gops.findIndex(g => g.timestamp === this.currentGop!.timestamp)
    if (idx >= 0) {
      for (const f of this.gops[idx].frames) this.codec.dispose(f)
      this.gops[idx] = this.currentGop
    } else {
      this.gops.push(this.currentGop)
      this.gops.sort((a, b) => a.timestamp - b.timestamp)
    }
    this.currentGop = null
  }

  clear() {
    for (const gop of this.gops)
      for (const f of gop.frames) this.codec.dispose(f)
    if (this.currentGop)
      for (const f of this.currentGop.frames) this.codec.dispose(f)
    this.gops = []
    this.currentGop = null
    this.decodedChunks.clear()
  }

  stats(): { gops: number, frames: number } {
    let frames = 0
    for (const gop of this.gops)
      frames += gop.frames.length
    return { gops: this.gops.length, frames }
  }

  private pushFrame(frame: F, gopTimestamp?: number) {
    if (gopTimestamp === undefined) {
      if (!this.currentGop) {
        this.codec.dispose(frame)
        return
      }
      this.currentGop.frames.push(frame)
      return
    }

    let gop = this.gops.find(g => g.timestamp === gopTimestamp)
    if (!gop) {
      gop = { timestamp: gopTimestamp, frames: [] }
      this.gops.push(gop)
      this.gops.sort((a, b) => a.timestamp - b.timestamp)
    }
    gop.frames.push(frame)
  }
}

export function computeNeededGops(
  available: number[], ts: number, rate: number, direction: 1 | -1,
): number[] {
  const currentGopIdx = findGopIndex(available, ts)
  if (currentGopIdx < 0) return []

  const lookahead = Math.max(1, Math.floor(rate))
  const needed: number[] = []
  const behindIdx = currentGopIdx - direction
  if (behindIdx >= 0 && behindIdx < available.length)
    needed.push(available[behindIdx])
  for (let i = 0; i <= lookahead; i++) {
    const targetIdx = currentGopIdx + (i * direction)
    if (targetIdx < 0 || targetIdx >= available.length) break
    needed.push(available[targetIdx])
  }
  return needed
}

function findGopIndex(timestamps: number[], ts: number): number {
  if (timestamps.length === 0) return -1
  let lo = 0
  let hi = timestamps.length - 1
  while (lo < hi) {
    const mid = (lo + hi + 1) >>> 1
    if (timestamps[mid] <= ts)
      lo = mid
    else
      hi = mid - 1
  }
  return timestamps[lo] <= ts ? lo : -1
}
