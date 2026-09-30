// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { formatShowcaseSummary, showcaseParam, SHOWCASE_FILTERS } from './showcaseFilter'

describe('showcaseParam (API_CONTRACT_CYCLE28.md §594.1)', () => {
  it('«Все» sends nothing (server default), the other two are sent as they are', () => {
    expect(showcaseParam('all')).toBeUndefined()
    expect(showcaseParam('only')).toBe('only')
    expect(showcaseParam('exclude')).toBe('exclude')
  })

  it('offers exactly the three values the server accepts, «Все» first', () => {
    expect(SHOWCASE_FILTERS.map((f) => f.value)).toEqual(['all', 'exclude', 'only'])
  })
})

describe('formatShowcaseSummary (ARCHITECTURE_CYCLE28.md §578)', () => {
  it('«Витрина: N компаний, M записей» with Russian plurals', () => {
    expect(formatShowcaseSummary({ showcaseCompanies: 9, showcaseBookings: 9412 })).toBe(
      'Витрина: 9 компаний, 9 412 записей',
    )
    expect(formatShowcaseSummary({ showcaseCompanies: 1, showcaseBookings: 21 })).toBe('Витрина: 1 компания, 21 запись')
    expect(formatShowcaseSummary({ showcaseCompanies: 2, showcaseBookings: 3 })).toBe('Витрина: 2 компании, 3 записи')
    expect(formatShowcaseSummary({ showcaseCompanies: 11, showcaseBookings: 12 })).toBe('Витрина: 11 компаний, 12 записей')
  })

  it('no line for an older server (fields absent) or an empty showcase', () => {
    expect(formatShowcaseSummary(undefined)).toBeNull()
    expect(formatShowcaseSummary({})).toBeNull()
    expect(formatShowcaseSummary({ showcaseCompanies: 0, showcaseBookings: 0 })).toBeNull()
  })
})
