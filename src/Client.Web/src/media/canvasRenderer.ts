export class CanvasRenderer {
  private canvas: HTMLCanvasElement | null = null
  private ctx: CanvasRenderingContext2D | null = null
  private bufferWidth = 0
  private bufferHeight = 0

  attach(canvas: HTMLCanvasElement) {
    this.canvas = canvas
    this.ctx = canvas.getContext('2d')
    this.bufferWidth = 0
    this.bufferHeight = 0
  }

  detach() {
    this.canvas = null
    this.ctx = null
    this.bufferWidth = 0
    this.bufferHeight = 0
  }

  renderFrame(frame: VideoFrame) {
    if (!this.canvas || !this.ctx) return

    if (frame.displayWidth !== this.bufferWidth || frame.displayHeight !== this.bufferHeight) {
      this.bufferWidth = frame.displayWidth
      this.bufferHeight = frame.displayHeight
      this.canvas.width = frame.displayWidth
      this.canvas.height = frame.displayHeight
      this.canvas.style.aspectRatio = `${frame.displayWidth / frame.displayHeight}`
    }

    this.ctx.drawImage(frame, 0, 0)
  }
}
