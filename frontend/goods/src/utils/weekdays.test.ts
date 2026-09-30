// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { isEveryDay, normalizeWeekdays, shortWeekdayOfDate, toggleWeekday } from './weekdays'

describe('weekdays', () => {
  it('normalizes to Monday-first without duplicates and drops unknown values', () => {
    expect(normalizeWeekdays(['Friday', 'Monday', 'Friday', 'Someday'])).toEqual(['Monday', 'Friday'])
  })
  it('toggles a day in and out keeping the order', () => {
    expect(toggleWeekday(['Monday', 'Friday'], 'Wednesday')).toEqual(['Monday', 'Wednesday', 'Friday'])
    expect(toggleWeekday(['Monday', 'Friday'], 'Monday')).toEqual(['Friday'])
  })
  it('an empty list is valid («только по меню») and is not «every day»', () => {
    expect(toggleWeekday(['Monday'], 'Monday')).toEqual([])
    expect(isEveryDay([])).toBe(false)
    expect(isEveryDay(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'])).toBe(true)
  })
  it('gives the weekday of a calendar date without a timezone shift (2026-09-30 is a Wednesday)', () => {
    expect(shortWeekdayOfDate('2026-09-30')).toBe('ср')
    expect(shortWeekdayOfDate('2026-10-04')).toBe('вс')
    expect(shortWeekdayOfDate('2026-10-05')).toBe('пн')
  })
})
