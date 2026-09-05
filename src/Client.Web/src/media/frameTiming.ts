export class FrameTimingRing {
  static readonly capacity = 120

  private readonly deltas = new Float64Array(FrameTimingRing.capacity)
  private count = 0
  private lastMark = 0

  mark() {
    const now = performance.now()
    if (this.lastMark > 0)
      this.deltas[this.count++ % FrameTimingRing.capacity] = now - this.lastMark
    this.lastMark = now
  }

  interrupt() {
    this.lastMark = 0
  }

  reset() {
    this.count = 0
    this.lastMark = 0
  }

  copy(dest: Float64Array): number {
    const count = Math.min(this.count, FrameTimingRing.capacity, dest.length)
    for (let i = 0; i < count; i++)
      dest[i] = this.deltas[(this.count - count + i) % FrameTimingRing.capacity]
    return count
  }
}
