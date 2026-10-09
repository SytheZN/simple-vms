import type { TimelineSpan, TimelineEvent } from '@/types/api'

export interface TimeRange {
  from: number
  to: number
}

const mutableTailUs = 5 * 60 * 1_000_000

export class TimelineCache {
  spans: TimelineSpan[] = []
  readonly events = new Map<string, TimelineEvent>()
  private covered: TimeRange[] = []
  private inFlight: TimeRange[] = []

  missing(range: TimeRange): TimeRange[] {
    return subtractRanges([range], [...this.covered, ...this.inFlight])
  }

  begin(range: TimeRange) {
    this.inFlight.push(range)
  }

  complete(range: TimeRange, spans: TimelineSpan[], events: TimelineEvent[], nowUs: number) {
    this.endInFlight(range)
    const settledTo = Math.min(range.to, nowUs - mutableTailUs)
    if (settledTo > range.from)
      this.covered = mergeRanges([...this.covered, { from: range.from, to: settledTo }])
    this.spans = mergeSpans([...this.spans, ...spans])
    for (const evt of events) this.events.set(evt.id, evt)
  }

  fail(range: TimeRange) {
    this.endInFlight(range)
  }

  private endInFlight(range: TimeRange) {
    const idx = this.inFlight.indexOf(range)
    if (idx >= 0) this.inFlight.splice(idx, 1)
  }
}

export function mergeSpans(spans: TimelineSpan[]): TimelineSpan[] {
  return mergeRanges(spans.map(s => ({ from: s.startTime, to: s.endTime })))
    .map(r => ({ startTime: r.from, endTime: r.to }))
}

export function mergeRanges(ranges: TimeRange[]): TimeRange[] {
  const sorted = [...ranges].sort((a, b) => a.from - b.from)
  const merged: TimeRange[] = []
  for (const r of sorted) {
    const last = merged[merged.length - 1]
    if (last && r.from <= last.to + 1)
      last.to = Math.max(last.to, r.to)
    else
      merged.push({ ...r })
  }
  return merged
}

export function subtractRanges(ranges: TimeRange[], holes: TimeRange[]): TimeRange[] {
  let remaining = ranges
  for (const hole of mergeRanges(holes)) {
    remaining = remaining.flatMap(r => {
      if (hole.to <= r.from || hole.from >= r.to) return [r]
      const pieces: TimeRange[] = []
      if (hole.from > r.from) pieces.push({ from: r.from, to: hole.from })
      if (hole.to < r.to) pieces.push({ from: hole.to, to: r.to })
      return pieces
    })
  }
  return remaining
}
