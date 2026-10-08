// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { arrivalGuestsText, isQuietDay, turnoverHouses } from './schedule'
import type { ScheduleArrivalDto, ScheduleDayDto } from '../types'

const arrival = (over: Partial<ScheduleArrivalDto> = {}): ScheduleArrivalDto => ({
  bookingId: 'b1', houseId: 'h1', houseName: 'Дом 1', checkInTime: '14:00', arrivalTime: null, guestName: 'Анна',
  adults: 2, children: 0, extraBeds: 0, dogs: 0, needCot: false, comment: null, paymentUnconfirmed: false, sameDayTurnover: false, turnoverText: null, ...over,
})

describe('schedule helpers', () => {
  it('names only the parts that apply', () => {
    expect(arrivalGuestsText(arrival())).toBe('2 взр.')
    expect(arrivalGuestsText(arrival({ adults: 3, children: 2, extraBeds: 1, dogs: 2, needCot: true }))).toBe('3 взр., 2 дет., доп. мест: 1, собак: 2, кроватка')
  })
  it('a quiet day has neither arrivals nor departures', () => {
    expect(isQuietDay({ arrivals: [], departures: [] })).toBe(true)
    expect(isQuietDay({ arrivals: [arrival()], departures: [] })).toBe(false)
  })
  it('collects the houses with a same-day turnover once', () => {
    const day: Pick<ScheduleDayDto, 'arrivals' | 'departures'> = {
      arrivals: [arrival({ sameDayTurnover: true }), arrival({ houseName: 'Дом 2' })],
      departures: [{ bookingId: 'b0', houseId: 'h1', houseName: 'Дом 1', checkOutTime: '12:00', sameDayTurnover: true }],
    }
    expect(turnoverHouses(day)).toEqual(['Дом 1'])
  })
})
