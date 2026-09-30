// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { HOME_CITY_KEY, parseCityId, readStoredCity, writeStoredCity } from './homeCity'

const store = (init: Record<string, string> = {}) => {
  const m = new Map(Object.entries(init))
  return { getItem: (k: string) => m.get(k) ?? null, setItem: (k: string, v: string) => void m.set(k, v), removeItem: (k: string) => void m.delete(k), m }
}
const perm = { id: 59, name: 'Пермь', region: 'Пермский край', label: 'Пермь, Пермский край', timeZoneId: 'Asia/Yekaterinburg', utcOffsetMinutes: 300 }

describe('homeCity', () => {
  it('parseCityId: none stays undefined, junk becomes an id that matches nothing', () => {
    expect(parseCityId(undefined)).toBeUndefined()
    expect(parseCityId('59')).toBe(59)
    expect(parseCityId('abc')).toBe(-1)
    expect(parseCityId('0')).toBe(-1)
  })

  it('round-trips a city under the goods key', () => {
    const s = store()
    writeStoredCity(perm, s)
    expect(s.m.has(HOME_CITY_KEY)).toBe(true)
    expect(readStoredCity(s)).toEqual(perm)
  })

  it('clears on null and survives corrupted storage', () => {
    const s = store({ [HOME_CITY_KEY]: '{oops' })
    expect(readStoredCity(s)).toBeNull()
    writeStoredCity(perm, s)
    writeStoredCity(null, s)
    expect(readStoredCity(s)).toBeNull()
  })

  it('rejects an entry without an id', () => {
    expect(readStoredCity(store({ [HOME_CITY_KEY]: JSON.stringify({ name: 'x' }) }))).toBeNull()
  })
})
