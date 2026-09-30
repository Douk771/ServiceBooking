import type { City } from '@/types'

/** Explicit key, separate from ezbook's `home-city` (ARCHITECTURE_CYCLE25.md §505.4). */
export const HOME_CITY_KEY = 'goods-home-city'

export function parseCityId(raw: string | undefined): number | undefined {
  if (raw === undefined) return undefined
  const n = Number(raw)
  return Number.isInteger(n) && n > 0 ? n : -1 // an unknown id is an empty list, not a 404 (§505.4)
}

export function readStoredCity(storage: Pick<Storage, 'getItem'> = localStorage): City | null {
  try {
    const raw = storage.getItem(HOME_CITY_KEY)
    if (!raw) return null
    const c = JSON.parse(raw) as Partial<City>
    return typeof c.id === 'number' && typeof c.label === 'string' && typeof c.name === 'string'
      ? { id: c.id, name: c.name, region: c.region ?? '', label: c.label, timeZoneId: c.timeZoneId ?? '', utcOffsetMinutes: c.utcOffsetMinutes ?? 0 }
      : null
  } catch {
    return null
  }
}

export function writeStoredCity(city: City | null, storage: Pick<Storage, 'setItem' | 'removeItem'> = localStorage): void {
  try {
    if (city) storage.setItem(HOME_CITY_KEY, JSON.stringify(city))
    else storage.removeItem(HOME_CITY_KEY)
  } catch {
    /* private mode: the choice just is not remembered */
  }
}
