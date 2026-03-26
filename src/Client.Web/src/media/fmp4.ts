export interface Sample {
  data: Uint8Array
  duration: number
  isKey: boolean
  timestamp: number
  wallClockUs: number
}

export interface CodecConfig {
  codec: string
  description: Uint8Array
  width: number
  height: number
}

export interface DemuxedGop {
  samples: Sample[]
  wallClockUs: number
}

export function parseInitSegment(data: Uint8Array): CodecConfig | null {
  const dbg = typeof localStorage !== 'undefined' && localStorage.getItem('debug_player') !== null

  const moov = findBox(data, 0, data.length, 'moov')
  if (!moov) { if (dbg) console.error('parseInit: no moov'); return null }

  const trak = findBox(data, moov.dataOffset, moov.end, 'trak')
  if (!trak) { if (dbg) console.error('parseInit: no trak'); return null }

  const tkhd = findFullBox(data, trak.dataOffset, trak.end, 'tkhd')
  let width = 0
  let height = 0
  if (tkhd) {
    const tkhdEnd = tkhd.dataOffset + tkhd.dataSize
    width = readUint16(data, tkhdEnd - 8)
    height = readUint16(data, tkhdEnd - 4)
  }

  const mdia = findBox(data, trak.dataOffset, trak.end, 'mdia')
  if (!mdia) { if (dbg) console.error('parseInit: no mdia'); return null }

  const minf = findBox(data, mdia.dataOffset, mdia.end, 'minf')
  if (!minf) { if (dbg) console.error('parseInit: no minf'); return null }

  const stbl = findBox(data, minf.dataOffset, minf.end, 'stbl')
  if (!stbl) { if (dbg) console.error('parseInit: no stbl'); return null }

  const stsd = findFullBox(data, stbl.dataOffset, stbl.end, 'stsd')
  if (!stsd) { if (dbg) console.error('parseInit: no stsd'); return null }

  const entryOffset = stsd.dataOffset + 4
  if (entryOffset + 8 > stsd.end) { if (dbg) console.error('parseInit: stsd too small'); return null }

  const entrySize = readUint32(data, entryOffset)
  const entryType = readFourCc(data, entryOffset + 4)
  if (dbg) console.log('parseInit: entry type', entryType, 'size', entrySize)

  let codec: string
  let configBoxType: string
  if (entryType === 'avc1' || entryType === 'avc3') {
    codec = 'avc1'
    configBoxType = 'avcC'
  } else if (entryType === 'hev1' || entryType === 'hvc1') {
    codec = 'hev1'
    configBoxType = 'hvcC'
  } else {
    if (dbg) console.error('parseInit: unknown entry type', entryType)
    return null
  }

  const entryDataStart = entryOffset + 8
  const entryEnd = entryOffset + entrySize
  const configSearchStart = entryDataStart + 78
  if (dbg) console.log('parseInit: searching for', configBoxType, 'in range', configSearchStart, '-', entryEnd)
  const configBox = findBox(data, configSearchStart, entryEnd, configBoxType)
  if (!configBox) {
    if (dbg) {
      console.error('parseInit: no', configBoxType, 'box found')
      let pos = entryDataStart + 8
      while (pos + 8 <= entryEnd) {
        const sz = readUint32(data, pos)
        const tp = readFourCc(data, pos + 4)
        console.log('  box at', pos - entryDataStart, ':', tp, 'size', sz)
        if (sz < 8) break
        pos += sz
      }
    }
    return null
  }

  const description = data.slice(configBox.dataOffset, configBox.end)
  if (dbg) console.log('parseInit: found', configBoxType, description.length, 'bytes, width', width, 'height', height)

  return { codec, description, width, height }
}

export function demuxGop(data: Uint8Array, timescale: number): DemuxedGop {
  const samples: Sample[] = []
  let wallClockUs = 0
  let firstBaseDecodeTime = -1
  let offset = 0

  while (offset < data.length) {
    const moof = findBox(data, offset, data.length, 'moof')
    if (!moof) break

    const traf = findBox(data, moof.dataOffset, moof.end, 'traf')
    if (!traf) { offset = moof.end; continue }

    let baseDecodeTime = 0
    const tfdt = findFullBox(data, traf.dataOffset, traf.end, 'tfdt')
    if (tfdt) {
      if (tfdt.version === 1)
        baseDecodeTime = Number(readUint64(data, tfdt.dataOffset))
      else
        baseDecodeTime = readUint32(data, tfdt.dataOffset)
    }

    const prft = findFullBox(data, moof.dataOffset, moof.end, 'prft')
    if (prft && prft.version === 1) {
      wallClockUs = Number(readUint64(data, prft.dataOffset + 4))
    }

    const trun = findFullBox(data, traf.dataOffset, traf.end, 'trun')
    if (!trun) { offset = moof.end; continue }

    const sampleCount = readUint32(data, trun.dataOffset)
    const flags = trun.flags

    const hasDataOffset = (flags & 0x000001) !== 0
    const hasFirstSampleFlags = (flags & 0x000004) !== 0
    const hasDuration = (flags & 0x000100) !== 0
    const hasSize = (flags & 0x000200) !== 0
    const hasSampleFlags = (flags & 0x000400) !== 0
    const hasCtsOffset = (flags & 0x000800) !== 0

    let pos = trun.dataOffset + 4
    let dataOffset = moof.offset
    if (hasDataOffset) {
      dataOffset = moof.offset + readInt32(data, pos)
      pos += 4
    }

    let firstSampleFlags = 0
    if (hasFirstSampleFlags) {
      firstSampleFlags = readUint32(data, pos)
      pos += 4
    }

    if (firstBaseDecodeTime < 0)
      firstBaseDecodeTime = baseDecodeTime

    let sampleDataOffset = dataOffset
    let currentTime = baseDecodeTime

    for (let i = 0; i < sampleCount; i++) {
      let duration = 0
      let size = 0
      let sampleFlags = 0
      let ctsOffset = 0

      if (hasDuration) { duration = readUint32(data, pos); pos += 4 }
      if (hasSize) { size = readUint32(data, pos); pos += 4 }
      if (hasSampleFlags) { sampleFlags = readUint32(data, pos); pos += 4 }
      if (hasCtsOffset) { ctsOffset = readInt32(data, pos); pos += 4 }

      const effectiveFlags = i === 0 && hasFirstSampleFlags ? firstSampleFlags : sampleFlags
      const isKey = (effectiveFlags & 0x02000000) !== 0

      const mediaOffsetUs = (currentTime + ctsOffset - firstBaseDecodeTime) / timescale * 1_000_000
      const durationUs = duration / timescale * 1_000_000
      const sampleWallClockUs = wallClockUs > 0 ? wallClockUs + mediaOffsetUs : 0

      const sampleData = data.slice(sampleDataOffset, sampleDataOffset + size)
      samples.push({
        data: sampleData,
        duration: durationUs,
        isKey,
        timestamp: sampleWallClockUs,
        wallClockUs,
      })

      sampleDataOffset += size
      currentTime += duration
    }

    offset = moof.end
    const mdat = findBox(data, offset, data.length, 'mdat')
    if (mdat) offset = mdat.end
  }

  return { samples, wallClockUs }
}

export function parseTimescale(data: Uint8Array): number {
  const moov = findBox(data, 0, data.length, 'moov')
  if (!moov) return 90000

  const mdia = findBoxDeep(data, moov.dataOffset, moov.end, ['trak', 'mdia'])
  if (!mdia) return 90000

  const mdhd = findFullBox(data, mdia.dataOffset, mdia.end, 'mdhd')
  if (!mdhd) return 90000

  if (mdhd.version === 1)
    return readUint32(data, mdhd.dataOffset + 16)
  return readUint32(data, mdhd.dataOffset + 8)
}

interface BoxInfo {
  offset: number
  size: number
  dataOffset: number
  end: number
}

interface FullBoxInfo extends BoxInfo {
  version: number
  flags: number
  dataSize: number
}

function findBox(data: Uint8Array, start: number, end: number, type: string): BoxInfo | null {
  let pos = start
  while (pos + 8 <= end) {
    const size = readUint32(data, pos)
    const boxType = readFourCc(data, pos + 4)
    if (size < 8) return null
    if (boxType === type)
      return { offset: pos, size, dataOffset: pos + 8, end: pos + size }
    pos += size
  }
  return null
}

function findFullBox(data: Uint8Array, start: number, end: number, type: string): FullBoxInfo | null {
  const box = findBox(data, start, end, type)
  if (!box || box.size < 12) return null
  const version = data[box.dataOffset]
  const flags = (data[box.dataOffset + 1] << 16) | (data[box.dataOffset + 2] << 8) | data[box.dataOffset + 3]
  const dataOffset = box.dataOffset + 4
  return { ...box, version, flags, dataOffset, dataSize: box.end - dataOffset }
}

function findBoxDeep(data: Uint8Array, start: number, end: number, path: string[]): BoxInfo | null {
  let current: BoxInfo = { offset: start, size: end - start, dataOffset: start, end }
  for (const type of path) {
    const found = findBox(data, current.dataOffset, current.end, type)
    if (!found) return null
    current = found
  }
  return current
}

function readUint32(data: Uint8Array, offset: number): number {
  return ((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]) >>> 0
}

function readInt32(data: Uint8Array, offset: number): number {
  return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]
}

function readUint16(data: Uint8Array, offset: number): number {
  return (data[offset] << 8) | data[offset + 1]
}

function readUint64(data: Uint8Array, offset: number): bigint {
  const hi = BigInt(readUint32(data, offset))
  const lo = BigInt(readUint32(data, offset + 4))
  return (hi << 32n) | lo
}

function readFourCc(data: Uint8Array, offset: number): string {
  return String.fromCharCode(data[offset], data[offset + 1], data[offset + 2], data[offset + 3])
}

export function buildCodecString(config: CodecConfig): string {
  const d = config.description
  if (config.codec === 'avc1' && d.length >= 4) {
    const profileIdc = d[1]
    const constraints = d[2]
    const levelIdc = d[3]
    return `avc1.${hex2(profileIdc)}${hex2(constraints)}${hex2(levelIdc)}`
  }
  if (config.codec === 'hev1' && d.length >= 13) {
    const generalProfileSpace = (d[1] >> 6) & 0x03
    const generalTierFlag = (d[1] >> 5) & 0x01
    const generalProfileIdc = d[1] & 0x1f
    const generalProfileCompat =
      ((d[2] << 24) | (d[3] << 16) | (d[4] << 8) | d[5]) >>> 0
    const generalConstraints = []
    for (let i = 6; i < 12; i++) generalConstraints.push(d[i])
    while (generalConstraints.length > 0 && generalConstraints[generalConstraints.length - 1] === 0)
      generalConstraints.pop()
    const generalLevelIdc = d[12]
    const prefix = ['', 'A', 'B', 'C'][generalProfileSpace]
    const tier = generalTierFlag ? 'H' : 'L'
    const constraintStr = generalConstraints.length > 0
      ? '.' + generalConstraints.map(b => b.toString(16).toUpperCase()).join('.')
      : ''
    return `hev1.${prefix}${generalProfileIdc}.${generalProfileCompat.toString(16).toUpperCase()}.${tier}${generalLevelIdc}${constraintStr}`
  }
  return config.codec
}

function hex2(n: number): string {
  return n.toString(16).padStart(2, '0')
}
