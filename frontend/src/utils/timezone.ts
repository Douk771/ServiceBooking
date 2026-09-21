/**
 * US-30 п. 3 — the derived time zone must be shown as text, not silently applied: "Барнаул → UTC+7,
 * Asia/Barnaul" is the example the spec itself uses (Barnaul is UTC+7, not Novosibirsk's UTC+7 either
 * — they only coincide numerically). `utcOffsetMinutes` comes from the server, never computed here.
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
