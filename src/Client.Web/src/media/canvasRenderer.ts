export class CanvasRenderer {
  private canvas: HTMLCanvasElement | null = null
  private ctx: CanvasRenderingContext2D | null = null
  private aspectRatio = 0

  attach(canvas: HTMLCanvasElement) {
    this.canvas = canvas
    this.ctx = canvas.getContext('2d')
    this.aspectRatio = 0
  }

  detach() {
    this.canvas = null
    this.ctx = null
    this.aspectRatio = 0
  }

  renderFrame(frame: VideoFrame) {
    if (!this.canvas || !this.ctx) return

    const ratio = frame.displayWidth / frame.displayHeight
    if (ratio !== this.aspectRatio) {
      this.aspectRatio = ratio
      this.canvas.width = frame.displayWidth
      this.canvas.height = frame.displayHeight
      const cssWidth = this.canvas.clientWidth
      if (cssWidth > 0)
        this.canvas.style.height = `${cssWidth / ratio}px`
    }

    this.ctx.drawImage(frame, 0, 0)
  }
}
