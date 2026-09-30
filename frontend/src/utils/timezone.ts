/**
 * US-30 п. 3 — the derived time zone must be shown as text, not silently applied: "Барнаул → UTC+7,
 * Asia/Barnaul" is the example the spec itself uses (Barnaul is UTC+7, not Novosibirsk's UTC+7 either
 * — they only coincide numerically). `utcOffsetMinutes` is shown from the server; the client computes an offset only for a manually typed IANA zone and only to decide whether to ask for confirmation (`utcOffsetMinutesOf`, ARCHITECTURE_CYCLE32.md §32.6.4).
 */
export function formatUtcOffset(utcOffsetMinutes: number): string {
  const sign = utcOffsetMinutes >= 0 ? '+' : '-'
  const abs = Math.abs(utcOffsetMinutes)
  const hours = Math.floor(abs / 60)
  const minutes = abs % 60
  return minutes === 0 ? `UTC${sign}${hours}` : `UTC${sign}${hours}:${String(minutes).padStart(2, '0')}`
}

export function formatCityTimeZone(cityLabel: string, utcOffsetMinutes: number, timeZoneId: string): string {
  return `${cityLabel} → ${formatUtcOffset(utcOffsetMinutes)}, ${timeZoneId}`
}

/** Client-side offset of an arbitrary IANA zone — used ONLY to decide whether to show the confirmation. */
export function utcOffsetMinutesOf(timeZoneId: string, at: Date = new Date()): number | null {
  try {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone: timeZoneId, timeZoneName: 'longOffset' }).formatToParts(at)
    const name = parts.find((p) => p.type === 'timeZoneName')?.value
    if (!name) return null
    if (name === 'GMT' || name === 'UTC') return 0
    const m = /^(?:GMT|UTC)([+-])(\d{1,2})(?::(\d{2}))?$/.exec(name)
    if (!m) return null
    const total = Number(m[2]) * 60 + Number(m[3] ?? 0)
    return m[1] === '-' ? -total : total
  } catch {
    return null
  }
}
