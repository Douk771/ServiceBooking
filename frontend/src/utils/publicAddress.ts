/**
 * API_CONTRACT_CYCLE26.md §565 — the public "city, address" line of a company card.
 * The city is prepended unless the address already starts with it (so "Барнаул, Барнаул, Ленина" never
 * happens). The original strings are shown; normalisation is only for the comparison.
 */
function normalize(s: string): string {
  return s.trim().toLowerCase().replace(/ё/g, 'е')
}

export function publicAddress(cityName?: string | null, address?: string | null): string {
  const city = cityName?.trim() ?? ''
  const addr = address?.trim() ?? ''
  if (!city) return addr
  if (!addr) return city

  const c = normalize(city)
  // One leading marker is dropped: «город », «г.», «г ».
  const a = normalize(addr).replace(/^(город\s+|г\.\s*|г\s+)/, '')
  if (a.startsWith(c)) {
    const rest = a.slice(c.length)
    if (rest === '' || /^[\s,]/.test(rest)) return addr
  }
  return `${city}, ${addr}`
}
