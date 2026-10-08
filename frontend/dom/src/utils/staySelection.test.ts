// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { EMPTY_RANGE, dayStateText, pickDay, priceShort, selectionProblemText } from './staySelection'
import { addDays, nightDates } from './stayDates'
import type { HouseCalendarDto } from '../types'

const TODAY = '2027-01-01'

function calendar(over: Partial<HouseCalendarDto> = {}, taken: Record<string, 'Occupied' | 'MayFreeUp'> = {}): HouseCalendarDto {
  const days = nightDates(TODAY, addDays(TODAY, 120)).map((date) => ({
    date,
    state: taken[date] ?? ('Free' as const),
    priceRub: 5000,
  }))
  return {
    houseId: 'h',
    today: TODAY,
    from: TODAY,
    to: addDays(TODAY, 120),
    minNights: 3,
    maxNights: 30,
    allowGapFill: true,
    allowSameDayCheckIn: true,
    lastNight: addDays(TODAY, 119),
    days,
    ...over,
  }
}

const taken = (from: string, to: string, kind: 'Occupied' | 'MayFreeUp' = 'Occupied') =>
  Object.fromEntries(nightDates(from, to).map((d) => [d, kind])) as Record<string, 'Occupied' | 'MayFreeUp'>

describe('pickDay — two clicks make a range', () => {
  const cal = calendar({}, taken('2027-01-10', '2027-01-14'))

  it('first click picks a free night as check-in', () => {
    expect(pickDay(cal, EMPTY_RANGE, '2027-01-05')).toEqual({ range: { checkIn: '2027-01-05', checkOut: null }, message: null })
  })

  it('second click on a later date completes the range, and the check-out may be a taken night (departure day)', () => {
    const r = pickDay(cal, { checkIn: '2027-01-07', checkOut: null }, '2027-01-10')
    expect(r.range).toEqual({ checkIn: '2027-01-07', checkOut: '2027-01-10' })
    expect(r.message).toBeNull()
  })

  it('a range over taken nights is refused with the server wording and keeps the check-in', () => {
    const r = pickDay(cal, { checkIn: '2027-01-05', checkOut: null }, '2027-01-12')
    expect(r.range).toEqual({ checkIn: '2027-01-05', checkOut: null })
    expect(r.message).toBe('Эти даты уже заняты. Выберите другие')
  })

  it('a stay shorter than the minimum is refused and says the minimum', () => {
    const r = pickDay(cal, { checkIn: '2027-01-05', checkOut: null }, '2027-01-06')
    expect(r.message).toBe('Минимальный срок проживания — 3 ночи')
  })

  it('a stay longer than the maximum is refused', () => {
    const r = pickDay(calendar({ maxNights: 5 }), { checkIn: '2027-02-01', checkOut: null }, '2027-02-10')
    expect(r.message).toBe('Максимальный срок проживания — 5 ночей')
  })

  it('a click on an earlier free date moves the check-in', () => {
    const r = pickDay(cal, { checkIn: '2027-01-07', checkOut: null }, '2027-01-03')
    expect(r.range).toEqual({ checkIn: '2027-01-03', checkOut: null })
  })

  it('after a complete range a click starts over', () => {
    const r = pickDay(cal, { checkIn: '2027-01-05', checkOut: '2027-01-08' }, '2027-01-20')
    expect(r.range).toEqual({ checkIn: '2027-01-20', checkOut: null })
  })

  it('a taken, held or unavailable date cannot start a stay and says why', () => {
    const c = calendar({}, { ...taken('2027-01-10', '2027-01-12'), ...taken('2027-01-20', '2027-01-21', 'MayFreeUp') })
    expect(pickDay(c, EMPTY_RANGE, '2027-01-10').message).toBe('Эти даты уже заняты. Выберите другие')
    expect(pickDay(c, EMPTY_RANGE, '2027-01-20').message).toMatch(/временно удержана/)
    expect(pickDay(c, EMPTY_RANGE, '2030-01-01').message).toBe('Дата недоступна для бронирования')
  })

  it('«today» cannot be the check-in when the company forbids same-day check-in', () => {
    const r = pickDay(calendar({ allowSameDayCheckIn: false }), EMPTY_RANGE, TODAY)
    expect(r.range).toEqual(EMPTY_RANGE)
    expect(r.message).toBe('Заезд в день бронирования недоступен — выберите дату с завтрашнего дня')
  })

  it('a gap shorter than the minimum is bookable when gap filling is on and both sides are taken', () => {
    const c = calendar({}, { ...taken('2027-01-10', '2027-01-14'), ...taken('2027-01-16', '2027-01-20') })
    expect(pickDay(c, { checkIn: '2027-01-14', checkOut: null }, '2027-01-16').range.checkOut).toBe('2027-01-16')
    const off = calendar({ allowGapFill: false }, { ...taken('2027-01-10', '2027-01-14'), ...taken('2027-01-16', '2027-01-20') })
    expect(pickDay(off, { checkIn: '2027-01-14', checkOut: null }, '2027-01-16').message).toMatch(/Минимальный/)
  })
})

describe('texts', () => {
  it('beyond-horizon text names the last check-out date as dd.mm.yyyy', () => {
    expect(selectionProblemText('BeyondHorizon', { minNights: 1, maxNights: 9, lastNight: '2027-12-31' })).toBe('Бронирование открыто до 01.01.2028')
  })
  it('day state is a word, never only a colour', () => {
    expect(['Free', 'MayFreeUp', 'Occupied', 'Unavailable'].map((s) => dayStateText(s as 'Free'))).toEqual([
      'Свободно',
      'Возможно освободится',
      'Занято',
      'Недоступно',
    ])
  })
  it('compact price for a cell', () => {
    expect([950, 5000, 5500, 12000, 12340].map(priceShort)).toEqual(['950', '5к', '5,5к', '12к', '12,3к'])
    expect(priceShort(null)).toBe('')
  })
})
