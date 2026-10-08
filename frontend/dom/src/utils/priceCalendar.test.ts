// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { buildPriceMonth, countUncoveredDays, inRanges } from './priceCalendar'

const periods = [
  { startDate: '2027-01-05', endDate: '2027-01-20', priceRub: 6000 },
  { startDate: '2027-01-10', endDate: '2027-01-10', priceRub: 9000 }, // a one-day period wins
]

describe('price calendar', () => {
  it('aligns the month to a Monday-first week and prices each night by the period that covers it', () => {
    // 1 Jan 2027 is a Friday → four empty cells (Mon..Thu) before it.
    const cells = buildPriceMonth('2027-01-01', { mode: 'ByDates', constantPriceRub: null }, periods, [], '2027-01-01')
    expect(cells.slice(0, 4)).toEqual([null, null, null, null])
    const byDate = Object.fromEntries(cells.filter(Boolean).map((c) => [c!.date, c!.priceRub]))
    expect(byDate['2027-01-04']).toBeNull()
    expect(byDate['2027-01-05']).toBe(6000)
    expect(byDate['2027-01-10']).toBe(9000)
    expect(byDate['2027-01-20']).toBe(6000)
    expect(byDate['2027-01-21']).toBeNull()
    expect(cells.filter(Boolean)).toHaveLength(31)
  })

  it('constant mode prices every date the same and never reports uncovered dates', () => {
    const cells = buildPriceMonth('2027-02-01', { mode: 'Constant', constantPriceRub: 5000 }, [], [{ startDate: '2027-02-01', endDate: '2027-02-28' }], '2027-01-01')
    expect(cells.filter(Boolean).every((c) => c!.priceRub === 5000 && !c!.uncovered)).toBe(true)
  })

  it('marks future dates with no price inside a server-reported range, not the past and not priced dates', () => {
    const uncovered = [{ startDate: '2027-01-02', endDate: '2027-01-04' }, { startDate: '2027-01-21', endDate: '2027-01-31' }]
    const cells = buildPriceMonth('2027-01-01', { mode: 'ByDates' }, periods, uncovered, '2027-01-03')
    const get = (d: string) => cells.find((c) => c?.date === d)!
    expect(get('2027-01-02')).toMatchObject({ past: true, uncovered: false })
    expect(get('2027-01-03')).toMatchObject({ past: false, uncovered: true })
    expect(get('2027-01-05')).toMatchObject({ uncovered: false })
    expect(get('2027-01-25')).toMatchObject({ uncovered: true })
  })

  it('range helpers', () => {
    expect(inRanges('2027-01-03', [{ startDate: '2027-01-02', endDate: '2027-01-04' }])).toBe(true)
    expect(inRanges('2027-01-05', [{ startDate: '2027-01-02', endDate: '2027-01-04' }])).toBe(false)
    expect(countUncoveredDays([{ startDate: '2027-01-02', endDate: '2027-01-04' }, { startDate: '2027-01-10', endDate: '2027-01-10' }])).toBe(4)
  })
})
