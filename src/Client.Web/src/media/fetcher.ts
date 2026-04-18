export interface GopEntry {
  timestamp: number
  chunks: Uint8Array[]
}

export class Fetcher {
  private gops: GopEntry[] = []
  private gaps: { from: number, to: number }[] = []
  private fetchInFlight = false
  private live = false
  private sendFetch: ((from: number, to: number) => void) | null = null
  private fetchResolve: (() => void) | null = null

  attach(sendFetch: (from: number, to: number) => void) {
    this.sendFetch = sendFetch
  }

  detach() {
    this.sendFetch = null
  }

  setTarget(fromUs: number, toUs: number) {
    const forward = toUs > fromUs

    if (this.gops.length > 1) {
      const containing = this.findGop(fromUs)
      if (containing) {
        const idx = this.gops.indexOf(containing)
        if (forward && idx > 1)
          this.gops.splice(0, idx - 1)
        else if (!forward && idx < this.gops.length - 2)
          this.gops.splice(idx + 2)
      }
    }

    if (this.fetchInFlight || !this.sendFetch || this.live) return

    if (forward) {
      const newest = this.newestTimestamp()
      if (newest === null || newest < toUs) {
        let from = newest !== null ? newest + 1 : fromUs
        from = this.skipGapsForward(from)
        this.fetchInFlight = true
        this.sendFetch(from, from + 30_000_000)
      }
    } else {
      const oldest = this.oldestTimestamp()
      if (oldest === null || oldest > toUs) {
        let from = oldest !== null ? oldest - 1 : fromUs
        from = this.skipGapsReverse(from)
        this.fetchInFlight = true
        this.sendFetch(from, from - 30_000_000)
      }
    }
  }

  handleFetchComplete() {
    this.fetchInFlight = false
    if (this.fetchResolve) {
      this.fetchResolve()
      this.fetchResolve = null
    }
  }

  fetchAt(ts: number): Promise<void> {
    if (!this.sendFetch) return Promise.resolve()
    if (this.fetchInFlight) {
      return new Promise(resolve => {
        const prev = this.fetchResolve
        this.fetchResolve = () => { prev?.(); resolve() }
      })
    }
    this.fetchInFlight = true
    this.sendFetch(ts, ts)
    return new Promise(resolve => { this.fetchResolve = resolve })
  }

  handleLive() {
    this.live = true
  }

  handleRecording() {
    this.live = false
  }

  handleGap(from: number, to: number) {
    this.gaps.push({ from, to })
  }

  appendData(timestamp: number, data: Uint8Array) {
    const existing = this.gops.find(g => g.timestamp === timestamp)
    if (existing) {
      existing.chunks.push(data)
      return
    }
    this.gops.push({ timestamp, chunks: [data] })
    this.gops.sort((a, b) => a.timestamp - b.timestamp)
  }

  findGop(timestamp: number): GopEntry | null {
    if (this.gops.length === 0) return null
    let lo = 0
    let hi = this.gops.length - 1
    while (lo < hi) {
      const mid = (lo + hi + 1) >>> 1
      if (this.gops[mid].timestamp <= timestamp)
        lo = mid
      else
        hi = mid - 1
    }
    return this.gops[lo].timestamp <= timestamp ? this.gops[lo] : null
  }

  mergedData(gop: GopEntry): Uint8Array {
    if (gop.chunks.length === 1) return gop.chunks[0]
    const totalLen = gop.chunks.reduce((sum, c) => sum + c.length, 0)
    const merged = new Uint8Array(totalLen)
    let offset = 0
    for (const chunk of gop.chunks) {
      merged.set(chunk, offset)
      offset += chunk.length
    }
    return merged
  }

  gopTimestamps(): number[] {
    return this.gops.map(g => g.timestamp)
  }

  oldestTimestamp(): number | null {
    return this.gops.length > 0 ? this.gops[0].timestamp : null
  }

  newestTimestamp(): number | null {
    return this.gops.length > 0 ? this.gops[this.gops.length - 1].timestamp : null
  }

  reset() {
    this.gops = []
    this.gaps = []
    this.fetchInFlight = false
    this.live = false
  }

  private skipGapsForward(ts: number): number {
    for (const gap of this.gaps) {
      if (ts >= gap.from && ts < gap.to)
        ts = gap.to
    }
    return ts
  }

  private skipGapsReverse(ts: number): number {
    for (const gap of this.gaps) {
      if (ts > gap.from && ts <= gap.to)
        ts = gap.from
    }
    return ts
  }
}
