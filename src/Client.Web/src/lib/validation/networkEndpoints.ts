import type { RemoteAccessMode } from '@/types/api'

export interface ValidationResult {
  valid: boolean
  error?: string
}

export const LOOPBACK_HOSTS: ReadonlySet<string> = new Set([
  'localhost',
  '127.0.0.1',
  '::1',
  'host.docker.internal'
])

const HOSTNAME_PATTERN =
  /^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/

const IPV4_PATTERN = /^(\d{1,3}\.){3}\d{1,3}$/

function isValidIPv4(value: string): boolean {
  if (!IPV4_PATTERN.test(value)) return false
  return value.split('.').every(octet => {
    const n = Number(octet)
    return Number.isInteger(n) && n >= 0 && n <= 255
  })
}

function isValidIPv6(value: string): boolean {
  if (!value.includes(':')) return false
  try {
    new URL(`http://[${value}]/`)
    return true
  } catch {
    return false
  }
}

export function isIpLiteral(value: string | undefined | null): boolean {
  if (!value) return false
  const trimmed = value.trim()
  return isValidIPv4(trimmed) || isValidIPv6(trimmed)
}

export function splitHost(value: string): string {
  if (value.startsWith('[')) {
    const close = value.indexOf(']')
    return close > 0 ? value.slice(1, close) : value
  }
  if ((value.match(/:/g) ?? []).length > 1) return value
  const colon = value.lastIndexOf(':')
  return colon >= 0 ? value.slice(0, colon) : value
}

function isIpv4LinkLocalOrLoopback(ip: string): boolean {
  if (ip.startsWith('127.')) return true
  if (ip.startsWith('169.254.')) return true
  return false
}

function isIpv6LoopbackOrLinkLocal(ip: string): boolean {
  const lower = ip.toLowerCase()
  if (lower === '::1') return true
  if (lower.startsWith('fe80:')) return true
  return false
}

export function validateHostOrIp(
  value: string,
  opts: { allowPort: boolean; fieldLabel: string }
): ValidationResult {
  const trimmed = value.trim()
  if (!trimmed) return { valid: false, error: `${opts.fieldLabel} cannot be empty` }

  let host = trimmed

  if (isValidIPv4(trimmed)) {
    host = trimmed
  } else if (trimmed.startsWith('[')) {
    const end = trimmed.indexOf(']')
    if (end < 0) return { valid: false, error: `${opts.fieldLabel} has malformed IPv6 literal` }
    host = trimmed.slice(1, end)
    if (end < trimmed.length - 1) {
      if (!opts.allowPort) return { valid: false, error: `${opts.fieldLabel} must not include a port` }
      const rest = trimmed.slice(end + 1)
      if (!rest.startsWith(':')) return { valid: false, error: `${opts.fieldLabel} has malformed port` }
      const port = Number(rest.slice(1))
      if (!Number.isInteger(port) || port < 1 || port > 65535)
        return { valid: false, error: `${opts.fieldLabel} port must be between 1 and 65535` }
    }
    if (!isValidIPv6(host)) return { valid: false, error: `${opts.fieldLabel} is not a valid IPv6 address` }
  } else {
    const colon = trimmed.lastIndexOf(':')
    if (colon >= 0 && !isValidIPv6(trimmed)) {
      if (!opts.allowPort) return { valid: false, error: `${opts.fieldLabel} must not include a port` }
      host = trimmed.slice(0, colon)
      const portStr = trimmed.slice(colon + 1)
      const port = Number(portStr)
      if (!Number.isInteger(port) || port < 1 || port > 65535)
        return { valid: false, error: `${opts.fieldLabel} port must be between 1 and 65535` }
    }
  }

  if (!host) return { valid: false, error: `${opts.fieldLabel} host cannot be empty` }

  if (LOOPBACK_HOSTS.has(host.toLowerCase()))
    return {
      valid: false,
      error: `${opts.fieldLabel} '${host}' is not reachable from other devices on your network`
    }

  if (isValidIPv4(host) && isIpv4LinkLocalOrLoopback(host))
    return {
      valid: false,
      error: `${opts.fieldLabel} '${host}' is not reachable from other devices on your network`
    }

  if (isValidIPv6(host) && isIpv6LoopbackOrLinkLocal(host))
    return {
      valid: false,
      error: `${opts.fieldLabel} '${host}' is not reachable from other devices on your network`
    }

  if (!isValidIPv4(host) && !isValidIPv6(host) && !HOSTNAME_PATTERN.test(host))
    return { valid: false, error: `${opts.fieldLabel} '${host}' is not a valid hostname or IP address` }

  return { valid: true }
}

export interface NormalizedServerAddress {
  url: string
  host: string
  scheme: 'http' | 'https'
  port: number
}

export function normalizeServerAddress(
  input: string,
  opts: { serverListenPort: number }
): { value: NormalizedServerAddress } | { error: string } {
  const trimmed = input.trim().replace(/\/+$/, '')
  if (!trimmed) return { error: 'Address cannot be empty' }

  let rest = trimmed
  let scheme: 'http' | 'https' = 'http'
  const schemeMatch = rest.match(/^(https?):\/\/(.*)$/i)
  if (schemeMatch) {
    scheme = schemeMatch[1].toLowerCase() as 'http' | 'https'
    rest = schemeMatch[2]
  } else if (/^[a-zA-Z][a-zA-Z0-9+.-]*:\/\//.test(rest)) {
    return { error: 'Only http:// and https:// schemes are supported' }
  }

  const pathAt = rest.indexOf('/')
  if (pathAt >= 0) rest = rest.slice(0, pathAt)

  if (!rest) return { error: 'Address must include a host' }

  let host: string
  let explicitPort: number | null = null

  if (rest.startsWith('[')) {
    const end = rest.indexOf(']')
    if (end < 0) return { error: 'Malformed IPv6 literal' }
    host = rest.slice(1, end)
    const after = rest.slice(end + 1)
    if (after) {
      if (!after.startsWith(':')) return { error: 'Malformed address after IPv6 literal' }
      const parsed = Number(after.slice(1))
      if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535)
        return { error: 'Port must be between 1 and 65535' }
      explicitPort = parsed
    }
    if (!isValidIPv6(host)) return { error: 'Invalid IPv6 address' }
  } else {
    const colons = (rest.match(/:/g) ?? []).length
    if (colons > 1) return { error: 'IPv6 addresses must be wrapped in [brackets]' }
    if (colons === 1) {
      const colon = rest.lastIndexOf(':')
      host = rest.slice(0, colon)
      const parsed = Number(rest.slice(colon + 1))
      if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535)
        return { error: 'Port must be between 1 and 65535' }
      explicitPort = parsed
    } else {
      host = rest
    }
  }

  if (!host) return { error: 'Address must include a host' }

  const hostIsIp = isValidIPv4(host) || isValidIPv6(host)
  if (!hostIsIp && !HOSTNAME_PATTERN.test(host))
    return { error: `'${host}' is not a valid hostname or IP address` }

  const port = explicitPort ?? (scheme === 'https' ? 443 : opts.serverListenPort)

  const schemeDefaultPort = scheme === 'https' ? 443 : 80
  const hostForUrl = host.includes(':') ? `[${host}]` : host
  const url = port === schemeDefaultPort
    ? `${scheme}://${hostForUrl}`
    : `${scheme}://${hostForUrl}:${port}`

  return { value: { url, host, scheme, port } }
}

export function validateExternalPort(
  value: number | undefined | null,
  mode: RemoteAccessMode
): ValidationResult {
  if (value === undefined || value === null)
    return { valid: false, error: 'External port is required' }
  if (!Number.isInteger(value))
    return { valid: false, error: 'External port must be an integer' }

  if (mode === 'upnp' && (value < 20000 || value > 60000))
    return {
      valid: false,
      error: 'External port must be between 20000 and 60000 in Automatic mode'
    }
  if (mode === 'manual' && (value < 1 || value > 65535))
    return { valid: false, error: 'External port must be between 1 and 65535' }

  return { valid: true }
}

export function validateRouterAddress(value: string): ValidationResult {
  const trimmed = value.trim()
  if (!trimmed) return { valid: false, error: 'Router address cannot be empty' }
  if (isValidIPv4(trimmed)) return { valid: true }
  if (HOSTNAME_PATTERN.test(trimmed)) return { valid: true }
  return { valid: false, error: 'Router address must be an IPv4 literal or a hostname' }
}

export function guessRouterAddress(browserHost: string): string | null {
  if (!isValidIPv4(browserHost)) return null
  const parts = browserHost.split('.')
  parts[3] = '1'
  return parts.join('.')
}

export const EXTERNAL_PORT_MIN_UPNP = 20000
export const EXTERNAL_PORT_MAX_UPNP = 60000
