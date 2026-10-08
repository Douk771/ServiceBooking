// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  addDays,
  addMonths,
  arrivalTimeOptions,
  daysInMonth,
  diffDays,
  formatBlockNights,
  formatCountdown,
  formatInstantInZone,
  formatStayRange,
  instantToZoned,
  isIsoDate,
  nightDates,
  nightsBetween,
  nightsLabel,
  pluralRu,
  weekdayMon0,
  zonedWallToUtcMs,
} from './stayDates'

describe('stayDates — calendar arithmetic on YYYY-MM-DD strings', () => {
  it('adds days across month and year ends and leap days', () => {
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDays('2028-02-28', 1)).toBe('2028-02-29')
    expect(addDays('2027-03-01', -1)).toBe('2027-02-28')
  })
  it('counts nights and lists their dates (check-out night excluded)', () => {
    expect(nightsBetween('2026-12-30', '2027-01-02')).toBe(3)
    expect(nightsBetween('2027-01-02', '2027-01-02')).toBe(0)
    expect(nightsBetween('2027-01-03', '2027-01-02')).toBe(0)
    expect(nightDates('2026-12-30', '2027-01-02')).toEqual(['2026-12-30', '2026-12-31', '2027-01-01'])
    expect(diffDays('2027-01-02', '2026-12-30')).toBe(-3)
  })
  it('validates dates strictly', () => {
    expect(isIsoDate('2027-02-29')).toBe(false)
    expect(isIsoDate('2028-02-29')).toBe(true)
    expect(isIsoDate('2027-1-1')).toBe(false)
  })
  it('weekday with Monday = 0', () => {
    expect(weekdayMon0('2027-01-01')).toBe(4) // Friday
    expect(weekdayMon0('2027-01-03')).toBe(6) // Sunday
  })
  it('month helpers', () => {
    expect(addMonths('2026-12-01', 1)).toBe('2027-01-01')
    expect(addMonths('2027-01-01', -1)).toBe('2026-12-01')
    expect(daysInMonth('2028-02-01')).toBe(29)
  })
})

describe('stayDates — Russian wording', () => {
  it('plural forms', () => {
    expect([1, 2, 4, 5, 11, 12, 21, 22, 25].map((n) => nightsLabel(n))).toEqual([
      '1 ночь',
      '2 ночи',
      '4 ночи',
      '5 ночей',
      '11 ночей',
      '12 ночей',
      '21 ночь',
      '22 ночи',
      '25 ночей',
    ])
    expect(pluralRu(0, 'a', 'b', 'c')).toBe('c')
  })
  it('stay range and block range', () => {
    expect(formatStayRange('2026-12-30', '2027-01-02')).toBe('30 дек 2026 – 2 янв 2027 · 3 ночи')
    expect(formatStayRange('2027-01-05', '2027-01-08')).toBe('5 янв – 8 янв 2027 · 3 ночи')
    // The block stores the check-out date as «по»; the owner reads the last night.
    expect(formatBlockNights('2027-01-10', '2027-01-14')).toBe('ночи с 10 янв по 13 янв')
    expect(formatBlockNights('2027-01-10', '2027-01-11')).toBe('ночь 10 янв')
  })
  it('countdown never goes negative', () => {
    expect(formatCountdown(90_000)).toBe('01:30')
    expect(formatCountdown(-5)).toBe('00:00')
    expect(formatCountdown(59_999)).toBe('00:59')
  })
  it('arrival options run from the check-in time to 23:30 in half hours', () => {
    const o = arrivalTimeOptions('14:00')
    expect(o[0]).toBe('14:00')
    expect(o[o.length - 1]).toBe('23:30')
    expect(o).toHaveLength(20)
    expect(arrivalTimeOptions('14:15')[0]).toBe('14:30')
  })
})

describe('stayDates — the booking zone, not the browser zone (UTC+7 day boundaries, R37-7)', () => {
  const tz = 'Asia/Novokuznetsk'
  it('wall clock to instant', () => {
    expect(new Date(zonedWallToUtcMs('2026-12-30', '00:00', tz)).toISOString()).toBe('2026-12-29T17:00:00.000Z')
    expect(new Date(zonedWallToUtcMs('2026-12-30', '14:00', tz)).toISOString()).toBe('2026-12-30T07:00:00.000Z')
  })
  it('instant to local date and time around local midnight', () => {
    expect(instantToZoned(Date.parse('2026-12-29T16:59:59Z'), tz)).toEqual({ date: '2026-12-29', time: '23:59' })
    expect(instantToZoned(Date.parse('2026-12-29T17:00:00Z'), tz)).toEqual({ date: '2026-12-30', time: '00:00' })
  })
  it('formats a deadline on the booking clock', () => {
    expect(formatInstantInZone('2026-12-30T07:30:00Z', tz)).toBe('30 дек, 14:30')
  })
})
