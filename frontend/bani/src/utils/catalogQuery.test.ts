import { describe, it, expect } from 'vitest'
import { hasActiveFilters, isIsoDate, parseCatalogFilters, resourceLink, toApiQuery, toSearchParams } from './catalogQuery'

const parse = (qs: string) => parseCatalogFilters(new URLSearchParams(qs))

describe('catalog filters of bani', () => {
  it('reads city, date and page', () => {
    expect(parse('city=7&date=2026-10-20&page=3')).toEqual({ cityId: 7, date: '2026-10-20', page: 3 })
  })

  it('falls back to defaults on garbage instead of breaking', () => {
    expect(parse('city=abc&date=2026-02-31&page=0')).toEqual({ cityId: null, date: null, page: 1 })
    expect(parse('city=-4&date=20.10.2026&page=1.5')).toEqual({ cityId: null, date: null, page: 1 })
    expect(parse('')).toEqual({ cityId: null, date: null, page: 1 })
  })

  it('isIsoDate rejects non-calendar dates', () => {
    expect(isIsoDate('2026-10-09')).toBe(true)
    expect(isIsoDate('2026-13-01')).toBe(false)
    expect(isIsoDate('2026-2-3')).toBe(false)
  })

  it('writes only non-default values, so the plain catalog stays "/"', () => {
    expect(toSearchParams({ cityId: null, date: null, page: 1 }).toString()).toBe('')
    expect(toSearchParams({ cityId: 7, date: '2026-10-20', page: 2 }).toString()).toBe('city=7&date=2026-10-20&page=2')
  })

  it('maps the city to the API cityId', () => {
    expect(toApiQuery({ cityId: 7, date: null, page: 2 })).toEqual({ cityId: 7, page: 2, pageSize: 12 })
    expect(toApiQuery({ cityId: null, date: '2026-10-20', page: 1 })).toEqual({ date: '2026-10-20', page: 1, pageSize: 12 })
  })

  it('says whether a filter narrows the list (page alone does not)', () => {
    expect(hasActiveFilters({ cityId: null, date: null, page: 4 })).toBe(false)
    expect(hasActiveFilters({ cityId: 1, date: null, page: 1 })).toBe(true)
    expect(hasActiveFilters({ cityId: null, date: '2026-10-20', page: 1 })).toBe(true)
  })

  it('carries the date to the resource page', () => {
    expect(resourceLink('/sever/chan', { date: null })).toBe('/sever/chan')
    expect(resourceLink('/sever/chan', { date: '2026-10-20' })).toBe('/sever/chan?date=2026-10-20')
  })
})
