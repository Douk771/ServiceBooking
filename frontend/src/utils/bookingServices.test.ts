import { describe, it, expect } from 'vitest'
import { formatBookingServiceNames, bookingTotalDuration } from './bookingServices'
import type { Booking } from '../types'

function booking(overrides: Partial<Booking> = {}): Booking {
  return {
    id: 'b1',
    companyId: 'c1',
    serviceId: 's1',
    serviceName: 'Стрижка',
    masterId: 'm1',
    masterName: 'Иван',
    clientName: 'Пётр',
    date: '2026-10-01',
    startTime: '10:00:00',
    endTime: '10:30:00',
    status: 'Confirmed',
    createdAt: '2026-09-01T00:00:00Z',
    ...overrides,
  }
}

describe('formatBookingServiceNames', () => {
  it('joins multiple services with a comma', () => {
    const b = booking({
      services: [
        { serviceId: 's1', name: 'Стрижка', durationMinutes: 30, price: 1500 },
        { serviceId: 's2', name: 'Окрашивание', durationMinutes: 60, price: 2500 },
      ],
    })
    expect(formatBookingServiceNames(b)).toBe('Стрижка, Окрашивание')
  })

  it('falls back to serviceName when services is missing (legacy/mocked data)', () => {
    expect(formatBookingServiceNames(booking())).toBe('Стрижка')
  })

  it('falls back to serviceName when services is an empty array', () => {
    expect(formatBookingServiceNames(booking({ services: [] }))).toBe('Стрижка')
  })
})

describe('bookingTotalDuration', () => {
  it('prefers the server-echoed totalDurationMinutes', () => {
    expect(bookingTotalDuration(booking({ totalDurationMinutes: 90 }))).toBe(90)
  })

  it('sums services durations when totalDurationMinutes is absent', () => {
    const b = booking({
      services: [
        { serviceId: 's1', name: 'Стрижка', durationMinutes: 30, price: 1500 },
        { serviceId: 's2', name: 'Окрашивание', durationMinutes: 60, price: 2500 },
      ],
    })
    expect(bookingTotalDuration(b)).toBe(90)
  })

  it('is undefined when neither is available', () => {
    expect(bookingTotalDuration(booking())).toBeUndefined()
  })
})
