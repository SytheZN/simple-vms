import { demuxGop, buildCodecString } from './fmp4'
import type { CodecConfig } from './fmp4'
import type { Fetcher } from './fetcher'
import { DecodeController, type ChunkDecoder } from './decodeController'

export interface DecodedFrame {
  frame: VideoFrame
  timestamp: number
  duration: number
}

export class Decoder implements ChunkDecoder<DecodedFrame> {
  onFrame: ((frame: DecodedFrame, gopTimestamp?: number) => void) | null = null

  private readonly controller: DecodeController<DecodedFrame>
  private decoder: VideoDecoder | null = null
  private codecConfig: CodecConfig | null = null
  private timescale = 90000
  private lastWallClockUs = 0
  private stride = 1
  private strideCounter = 0

  constructor(fetcher: Fetcher) {
    this.controller = new DecodeController(fetcher, this)
  }

  configure(config: CodecConfig) {
    this.codecConfig = config
    this.flush()
    this.decoder = new VideoDecoder({
      output: (frame) => {
        if (this.stride > 1) {
          if (this.strideCounter % this.stride !== 0) {
            frame.close()
            this.strideCounter++
            return
          }
          this.strideCounter++
        }
        this.onFrame?.({
          frame,
          timestamp: frame.timestamp ?? 0,
          duration: frame.duration ?? 0,
        })
      },
      error: (e) => console.error('video decoder error', e),
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
    if (this.codecConfig) this.configure(this.codecConfig)
  }

  setTarget(gopTimestamps: number[]) {
    this.controller.setTarget(gopTimestamps)
  }

  getFrame(ts: number): DecodedFrame | null {
    return this.controller.getFrame(ts)
  }

  hasFrameToward(ts: number, direction: 1 | -1): boolean {
    return this.controller.hasFrameToward(ts, direction)
  }

  decode(data: Uint8Array, gopTimestamp: number) {
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

    if (this.controller.beginGop(gopTimestamp))
      this.strideCounter = 0

    for (const sample of demuxed.samples) {
      this.decoder.decode(new EncodedVideoChunk({
        type: sample.isKey ? 'key' : 'delta',
        timestamp: sample.timestamp,
        duration: sample.duration,
        data: sample.data,
      }))
    }
  }

  decodeKeyframe(data: Uint8Array, gopTimestamp: number) {
    if (!this.decoder || this.decoder.state !== 'configured') return
    const demuxed = demuxGop(data, this.timescale)
    if (demuxed.samples.length === 0) return
    const key = demuxed.samples.find(s => s.isKey)
    if (!key) return

    if (key.timestamp > 0) this.lastWallClockUs = key.timestamp + key.duration

    this.controller.restartGop(gopTimestamp)
    this.strideCounter = 0

    this.decoder.decode(new EncodedVideoChunk({
      type: 'key',
      timestamp: key.timestamp,
      duration: key.duration,
      data: key.data,
    }))
  }

  dispose(frame: DecodedFrame) {
    frame.frame.close()
  }

  resetWallClock() {
    this.lastWallClockUs = 0
  }

  flush() {
    this.controller.clear()
    if (this.decoder && this.decoder.state !== 'closed') {
      try { this.decoder.close() } catch {}
    }
    this.decoder = null
  }

  async finalize() {
    if (!this.decoder || this.decoder.state !== 'configured') return
    await this.decoder.flush()
    this.controller.finalizeCurrent()
  }

  stats(): { gops: number, frames: number } {
    return this.controller.stats()
  }
}
