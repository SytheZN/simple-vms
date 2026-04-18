import { demuxGop, buildCodecString } from './fmp4'
import type { CodecConfig } from './fmp4'
import type { Fetcher } from './fetcher'

export interface DecodedFrame {
  frame: VideoFrame
  timestamp: number
  duration: number
}

interface DecodedGop {
  timestamp: number
  frames: DecodedFrame[]
}

export class Decoder {
  private readonly fetcher: Fetcher
  private decoder: VideoDecoder | null = null
  private gops: DecodedGop[] = []
  private currentGop: DecodedGop | null = null
  private codecConfig: CodecConfig | null = null
  private timescale = 90000
  private lastWallClockUs = 0
  private stride = 1
  private strideCounter = 0
  private decodedChunks = new Map<number, number>()

  constructor(fetcher: Fetcher) {
    this.fetcher = fetcher
  }

  configure(config: CodecConfig) {
    this.codecConfig = config
    this.flush()
    this.decoder = new VideoDecoder({
      output: (frame) => {
        if (!this.currentGop) {
          frame.close()
          return
        }
        if (this.stride > 1) {
          if (this.strideCounter % this.stride !== 0) {
            frame.close()
            this.strideCounter++
            return
          }
          this.strideCounter++
        }
        this.currentGop.frames.push({
          frame,
          timestamp: frame.timestamp ?? 0,
          duration: frame.duration ?? 0,
        })
      },
      error: () => {},
    })
    this.decoder.configure({
      codec: buildCodecString(config),
      description: config.description,
      codedWidth: config.width,
      codedHeight: config.height,
    })
  }

  setTimescale(ts: number) {
    this.timescale = ts
  }

  setStride(newStride: number) {
    if (newStride === this.stride) return
    this.stride = newStride
    this.flushDecoded()
    if (this.codecConfig) this.configure(this.codecConfig)
  }

  setTarget(gopTimestamps: number[]) {
    const targetSet = new Set(gopTimestamps)
    const maxKeep = gopTimestamps.length + 2
    if (this.gops.length > maxKeep) {
      const toRemove = this.gops
        .filter(g => !targetSet.has(g.timestamp))
        .slice(0, this.gops.length - maxKeep)
      for (const gop of toRemove) {
        for (const f of gop.frames) f.frame.close()
        this.gops.splice(this.gops.indexOf(gop), 1)
        this.decodedChunks.delete(gop.timestamp)
      }
    }

    for (const gopTs of gopTimestamps) {
      const gop = this.fetcher.findGop(gopTs)
      if (!gop || gop.timestamp !== gopTs) continue

      const decoded = this.decodedChunks.get(gopTs) ?? 0
      if (decoded >= gop.chunks.length) continue

      for (let i = decoded; i < gop.chunks.length; i++)
        this.decodeRaw(gop.chunks[i], gopTs)
      this.decodedChunks.set(gopTs, gop.chunks.length)
    }
  }

  getFrame(ts: number): DecodedFrame | null {
    let best: DecodedFrame | null = null
    let bestDist = Infinity

    const search = (gop: DecodedGop) => {
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

  decodeKeyframe(data: Uint8Array, gopTimestamp: number) {
    if (!this.decoder || this.decoder.state !== 'configured') return
    const demuxed = demuxGop(data, this.timescale)
    if (demuxed.samples.length === 0) return
    const key = demuxed.samples.find(s => s.isKey)
    if (!key) return

    if (key.timestamp > 0) this.lastWallClockUs = key.timestamp + key.duration

    this.finalizeCurrentGop()
    this.currentGop = { timestamp: gopTimestamp, frames: [] }
    this.strideCounter = 0

    this.decoder.decode(new EncodedVideoChunk({
      type: 'key',
      timestamp: key.timestamp,
      duration: key.duration,
      data: key.data,
    }))
  }

  resetWallClock() {
    this.lastWallClockUs = 0
  }

  flush() {
    this.flushDecoded()
    if (this.decoder && this.decoder.state !== 'closed') {
      try { this.decoder.close() } catch {}
    }
    this.decoder = null
  }

  async finalize() {
    if (!this.decoder || this.decoder.state !== 'configured') return
    await this.decoder.flush()
    this.finalizeCurrentGop()
  }

  private flushDecoded() {
    for (const gop of this.gops)
      for (const f of gop.frames) f.frame.close()
    if (this.currentGop)
      for (const f of this.currentGop.frames) f.frame.close()
    this.gops = []
    this.currentGop = null
    this.decodedChunks.clear()
  }

  private decodeRaw(data: Uint8Array, gopTimestamp: number) {
    if (!this.decoder || this.decoder.state !== 'configured') return
    const demuxed = demuxGop(data, this.timescale)
    if (demuxed.samples.length === 0) return

    const hasWallClock = demuxed.samples[0].timestamp > 0
    if (hasWallClock) {
      const last = demuxed.samples[demuxed.samples.length - 1]
      this.lastWallClockUs = last.timestamp + last.duration
    } else if (this.lastWallClockUs > 0) {
      for (const sample of demuxed.samples) {
        sample.timestamp = this.lastWallClockUs
        this.lastWallClockUs += sample.duration
      }
    }

    if (!this.currentGop || this.currentGop.timestamp !== gopTimestamp) {
      this.finalizeCurrentGop()
      this.currentGop = { timestamp: gopTimestamp, frames: [] }
      this.strideCounter = 0
    }

    for (const sample of demuxed.samples) {
      this.decoder.decode(new EncodedVideoChunk({
        type: sample.isKey ? 'key' : 'delta',
        timestamp: sample.timestamp,
        duration: sample.duration,
        data: sample.data,
      }))
    }
  }

  private finalizeCurrentGop() {
    if (!this.currentGop || this.currentGop.frames.length === 0) return
    const idx = this.gops.findIndex(g => g.timestamp === this.currentGop!.timestamp)
    if (idx >= 0) {
      for (const f of this.gops[idx].frames) f.frame.close()
      this.gops[idx] = this.currentGop
    } else {
      this.gops.push(this.currentGop)
      this.gops.sort((a, b) => a.timestamp - b.timestamp)
    }
    this.currentGop = null
  }
}
