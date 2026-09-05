import type { Fetcher } from './fetcher'
import { DecodeController, type ChunkDecoder } from './decodeController'

export interface MotionFrame {
  timestamp: number
  cells: Uint8Array
  cols: number
  rows: number
  sync: boolean
}

const headerSize = 22
const maxChains = 64

export class MotionDecoder implements ChunkDecoder<MotionFrame> {
  onFrame: ((frame: MotionFrame, gopTimestamp?: number) => void) | null = null

  private readonly controller: DecodeController<MotionFrame>
  private readonly chains = new Map<number, Uint8Array>()
  private queue: Promise<void> = Promise.resolve()

  constructor(fetcher: Fetcher) {
    this.controller = new DecodeController(fetcher, this)
  }

  setTarget(gopTimestamps: number[]) {
    this.controller.setTarget(gopTimestamps)
  }

  getFrame(ts: number): MotionFrame | null {
    return this.controller.getFrame(ts)
  }

  stats(): { gops: number, frames: number } {
    return this.controller.stats()
  }

  flush() {
    this.controller.clear()
    this.chains.clear()
    this.queue = Promise.resolve()
  }

  decode(data: Uint8Array, gopTimestamp: number) {
    this.queue = this.queue
      .then(() => this.decodeChunk(data, gopTimestamp))
      .catch(e => console.error('motion decode failed', e))
  }

  dispose(_frame: MotionFrame) {}

  private async decodeChunk(data: Uint8Array, gopTimestamp: number) {
    let offset = 0
    while (offset < data.length) {
      if (data.length - offset < headerSize) {
        console.warn('motion gop has', data.length - offset, 'trailing bytes')
        return
      }
      if (data[offset] !== 0x4D || data[offset + 1] !== 0x47
        || data[offset + 2] !== 0x52 || data[offset + 3] !== 0x44) {
        console.warn('motion gop has no MGRD magic at offset', offset)
        return
      }
      const view = new DataView(data.buffer, data.byteOffset + offset)
      const flags = data[offset + 5]
      const timestamp = Number(view.getBigUint64(6, true))
      const cols = view.getUint16(14, true)
      const rows = view.getUint16(16, true)
      const payloadLength = view.getUint32(18, true)
      if (data.length - offset < headerSize + payloadLength) {
        console.warn('motion gop truncated', data.length - offset, 'of', headerSize + payloadLength, 'bytes')
        return
      }

      const cells = await inflate(data.slice(offset + headerSize, offset + headerSize + payloadLength))
      offset += headerSize + payloadLength
      if (cells.length !== cols * rows) {
        console.warn('motion cell count mismatch', cells.length, 'for', `${cols}x${rows}`)
        continue
      }

      const sync = (flags & 0x01) !== 0
      const base = sync ? null : this.chains.get(gopTimestamp)
      for (let i = 0; i < cells.length; i++)
        cells[i] ^= base ? base[i] : 0
      this.setChain(gopTimestamp, cells)

      this.onFrame?.({ timestamp, cells, cols, rows, sync }, gopTimestamp)
    }
  }

  private setChain(gopTimestamp: number, cells: Uint8Array) {
    this.chains.delete(gopTimestamp)
    this.chains.set(gopTimestamp, cells)
    while (this.chains.size > maxChains)
      this.chains.delete(this.chains.keys().next().value!)
  }
}

async function inflate(data: Uint8Array): Promise<Uint8Array> {
  const ds = new DecompressionStream('deflate-raw')
  const writer = ds.writable.getWriter()
  const reader = ds.readable.getReader()
  writer.write(data as Uint8Array<ArrayBuffer>)
  writer.close()
  const chunks: Uint8Array[] = []
  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    chunks.push(value)
  }
  const total = chunks.reduce((n, c) => n + c.length, 0)
  const out = new Uint8Array(total)
  let offset = 0
  for (const chunk of chunks) { out.set(chunk, offset); offset += chunk.length }
  return out
}
