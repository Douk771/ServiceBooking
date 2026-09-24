/**
 * ARCHITECTURE_CYCLE13.md §205/§239 (R3, US-132/133/139). The ONLY place in the product that
 * assembles links to third-party map services — templates live in two constants so a format change
 * is a one-line edit, and every call site goes through the pure functions below instead of
 * concatenating a URL itself.
 *
 * §205.1 п. 5 / §239 (licence condition, not a style preference): the URL carries ONLY the city and
 * the address — no `utm`, no user id, no product metrics, nothing parsed out of a map response.
 */

export interface MapTarget {
  address: string
  cityName?: string | null
  point?: { latitude: number; longitude: number } | null
}

const YANDEX_MAPS_BASE = 'https://yandex.ru/maps/'
const TWO_GIS_BASE = 'https://2gis.ru/search/'

/** "город, адрес" — order fixed by §5 SPEC / §239: "ул. Ленина, 5" without a city can open a
 *  same-named street in an entirely different region. */
function buildQuery(t: MapTarget): string {
  return [t.cityName, t.address].filter(Boolean).join(', ')
}

/**
 * ARCHITECTURE_CYCLE13.md §205 — coordinates go in `lon,lat` order for BOTH services; this is the
 * classic transposition bug, which is why `mapLinks.test.ts` uses deliberately asymmetric numbers
 * (53.34 / 83.77 can't silently swap without the test catching it).
 */
export function buildYandexMapsUrl(t: MapTarget): string {
  if (t.point) {
    const ll = `${t.point.longitude},${t.point.latitude}`
    return `${YANDEX_MAPS_BASE}?ll=${encodeURIComponent(ll)}&z=17&pt=${encodeURIComponent(ll)}&l=map`
  }
  return `${YANDEX_MAPS_BASE}?text=${encodeURIComponent(buildQuery(t))}`
}

/**
 * §205/§206 (R4): 2ГИС always goes through text search, even when a point is known — 2ГИС's coverage
 * doesn't match the product's 301-city directory, and "nothing found" needs to read as THEIR search
 * result, not as our product being broken. The point, when known, only nudges the map's centre.
 */
export function buildTwoGisUrl(t: MapTarget): string {
  const url = `${TWO_GIS_BASE}${encodeURIComponent(buildQuery(t))}`
  if (t.point) {
    return `${url}?m=${encodeURIComponent(`${t.point.longitude},${t.point.latitude}/17`)}`
  }
  return url
}
