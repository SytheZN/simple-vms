export function enrollmentLink(address: string, token: string): string {
  const params = new URLSearchParams({ address, token })
  return `svms://enroll?${params}`
}

export function isMobileBrowser(): boolean {
  if (matchMedia('(pointer: coarse) and (hover: none)').matches) return true
  const uaData = (navigator as Navigator & { userAgentData?: { mobile: boolean } }).userAgentData
  if (uaData?.mobile) return true
  return /Android|iPhone|iPad|iPod/i.test(navigator.userAgent)
}
