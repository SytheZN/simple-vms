export const hasWebCodecs = typeof VideoDecoder !== 'undefined'

export class MseFallbackPlayer {
  private mediaSource: MediaSource | null = null
  private sourceBuffer: SourceBuffer | null = null
  private video: HTMLVideoElement | null = null
  private queue: ArrayBuffer[] = []

  attach(video: HTMLVideoElement, mimeType: string): Promise<void> {
    return new Promise((resolve, reject) => {
      this.video = video
      this.mediaSource = new MediaSource()
      video.src = URL.createObjectURL(this.mediaSource)

      this.mediaSource.addEventListener('sourceopen', () => {
        if (!this.mediaSource || this.mediaSource.readyState !== 'open') {
          reject(new Error('MediaSource not open'))
          return
        }
        try {
          this.sourceBuffer = this.mediaSource.addSourceBuffer(mimeType)
          this.sourceBuffer.mode = 'sequence'
          this.sourceBuffer.addEventListener('updateend', () => this.appendNext())
          resolve()
        } catch (e) {
          reject(e)
        }
      })
    })
  }

  appendData(data: Uint8Array) {
    this.queue.push(new Uint8Array(data).buffer as ArrayBuffer)
    this.appendNext()
  }

  private appendNext() {
    if (!this.sourceBuffer || this.sourceBuffer.updating || this.queue.length === 0) return
    const chunk = this.queue.shift()!
    try {
      this.sourceBuffer.appendBuffer(chunk)
    } catch {
      this.queue = []
    }
  }

  play() {
    if (this.video) {
      this.video.muted = true
      this.video.play().catch(() => {})
    }
  }

  detach() {
    this.queue = []
    if (this.sourceBuffer) {
      this.sourceBuffer = null
    }
    if (this.mediaSource) {
      if (this.mediaSource.readyState === 'open') {
        try { this.mediaSource.endOfStream() } catch {}
      }
      this.mediaSource = null
    }
    if (this.video) {
      URL.revokeObjectURL(this.video.src)
      this.video.src = ''
      this.video = null
    }
  }
}
