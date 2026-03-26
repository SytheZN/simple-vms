export const ClientMsg = {
  Live: 0x01,
  Fetch: 0x02,
} as const

export const ServerMsg = {
  Init: 0x01,
  Gop: 0x02,
  Status: 0x03,
} as const

export const Status = {
  Ack: 0x00,
  FetchComplete: 0x01,
  Gap: 0x02,
  Error: 0x04,
  Live: 0x05,
  Recording: 0x06,
} as const

export function encodeLive(profile: string): ArrayBuffer {
  const profileBytes = new TextEncoder().encode(profile)
  const buf = new ArrayBuffer(2 + profileBytes.length)
  const view = new Uint8Array(buf)
  view[0] = ClientMsg.Live
  view[1] = profileBytes.length
  view.set(profileBytes, 2)
  return buf
}

export function encodeFetch(profile: string, from: number, to: number): ArrayBuffer {
  const profileBytes = new TextEncoder().encode(profile)
  const buf = new ArrayBuffer(2 + profileBytes.length + 16)
  const view = new DataView(buf)
  const bytes = new Uint8Array(buf)
  bytes[0] = ClientMsg.Fetch
  bytes[1] = profileBytes.length
  bytes.set(profileBytes, 2)
  view.setBigUint64(2 + profileBytes.length, BigInt(Math.floor(from)))
  view.setBigUint64(2 + profileBytes.length + 8, BigInt(Math.floor(to)))
  return buf
}

export interface ParsedInit {
  profile: string
  data: Uint8Array
}

export interface ParsedGop {
  flags: number
  profile: string
  timestamp: number
  data: Uint8Array
}

export interface ParsedStatus {
  code: number
  gapFrom?: number
  gapTo?: number
}

export function parseServerMessage(buf: ArrayBuffer): { type: number } & (
  | { type: 0x01 } & ParsedInit
  | { type: 0x02 } & ParsedGop
  | { type: 0x03 } & ParsedStatus
) {
  const data = new Uint8Array(buf)
  const type = data[0]

  if (type === ServerMsg.Init) {
    const profileLen = data[1]
    const profile = new TextDecoder().decode(data.slice(2, 2 + profileLen))
    const initData = data.slice(2 + profileLen)
    return { type, profile, data: initData } as any
  }

  if (type === ServerMsg.Gop) {
    const flags = data[1]
    const profileLen = data[2]
    const profile = new TextDecoder().decode(data.slice(3, 3 + profileLen))
    const view = new DataView(buf)
    const timestamp = Number(view.getBigUint64(3 + profileLen))
    const gopData = data.slice(3 + profileLen + 8)
    return { type, flags, profile, timestamp, data: gopData } as any
  }

  if (type === ServerMsg.Status) {
    const code = data[1]
    if (code === Status.Gap && data.length >= 18) {
      const view = new DataView(buf)
      const gapFrom = Number(view.getBigUint64(2))
      const gapTo = Number(view.getBigUint64(10))
      return { type, code, gapFrom, gapTo } as any
    }
    return { type, code } as any
  }

  return { type } as any
}
