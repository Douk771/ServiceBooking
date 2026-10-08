// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { hasActiveFilters, houseLink, parseCatalogFilters, toApiQuery, toSearchParams } from './catalogQuery'

const parse = (qs: string) => parseCatalogFilters(new URLSearchParams(qs))

describe('catalog filters in the URL', () => {
  it('an empty query is the plain catalog', () => {
    expect(parse('')).toEqual({ checkIn: null, checkOut: null, guests: 1, maxPrice: null, page: 1 })
    expect(hasActiveFilters(parse(''))).toBe(false)
    expect(toSearchParams(parse('')).toString()).toBe('')
  })

  it('round-trips a full filter set', () => {
    const f = parse('checkIn=2027-01-05&checkOut=2027-01-08&guests=4&maxPrice=7000&page=2')
    expect(f).toEqual({ checkIn: '2027-01-05', checkOut: '2027-01-08', guests: 4, maxPrice: 7000, page: 2 })
    expect(parse(toSearchParams(f).toString())).toEqual(f)
    expect(hasActiveFilters(f)).toBe(true)
  })

  it('dates are both-or-none and must be a real stay', () => {
    expect(parse('checkIn=2027-01-05').checkIn).toBeNull()
    expect(parse('checkOut=2027-01-08').checkOut).toBeNull()
    expect(parse('checkIn=2027-01-08&checkOut=2027-01-05').checkIn).toBeNull()
    expect(parse('checkIn=2027-02-30&checkOut=2027-03-02').checkIn).toBeNull()
  })

  it('malformed numbers fall back to the defaults instead of breaking the page', () => {
    expect(parse('guests=abc&maxPrice=-5&page=0').guests).toBe(1)
    expect(parse('guests=61').guests).toBe(1)
    expect(parse('maxPrice=0').maxPrice).toBeNull()
    expect(parse('page=x').page).toBe(1)
  })

  it('the price filter is maxPrice in the page URL and maxPricePerNight for the API', () => {
    expect(toApiQuery(parse('maxPrice=7000&guests=3'))).toEqual({ guests: 3, page: 1, pageSize: 12, maxPricePerNight: 7000 })
    expect(toApiQuery(parse('checkIn=2027-01-05&checkOut=2027-01-08'))).toMatchObject({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
  })

  it('a house link carries the dates and the guests over (houseQueryParams)', () => {
    expect(houseLink('/lesnoy/dom-1', parse('checkIn=2027-01-05&checkOut=2027-01-08&guests=4'))).toBe(
      '/lesnoy/dom-1?checkIn=2027-01-05&checkOut=2027-01-08&adults=4',
    )
    expect(houseLink('/lesnoy/dom-1', parse(''))).toBe('/lesnoy/dom-1')
  })
})
