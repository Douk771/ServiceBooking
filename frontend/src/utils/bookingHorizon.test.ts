import { describe, it, expect } from 'vitest'
import { parseBookingHorizonInput, parseHorizonExceededDays, lastBookableDateUtc, HORIZON_OUT_OF_RANGE_MESSAGE } from './bookingHorizon'

describe('parseBookingHorizonInput', () => {
  it('treats an empty string as "use the default" (sent as 0)', () => {
    expect(parseBookingHorizonInput('')).toEqual({ value: 0 })
    expect(parseBookingHorizonInput('   ')).toEqual({ value: 0 })
  })

  it('treats an explicit 0 the same as blank', () => {
    expect(parseBookingHorizonInput('0')).toEqual({ value: 0 })
  })

  it('accepts values within 1..365', () => {
    expect(parseBookingHorizonInput('1')).toEqual({ value: 1 })
    expect(parseBookingHorizonInput('90')).toEqual({ value: 90 })
    expect(parseBookingHorizonInput('365')).toEqual({ value: 365 })
  })

  it('rejects values above 365', () => {
    const result = parseBookingHorizonInput('366')
    expect(result.error).toBe(HORIZON_OUT_OF_RANGE_MESSAGE)
  })

  it('rejects negative values', () => {
    const result = parseBookingHorizonInput('-5')
    expect(result.error).toBe(HORIZON_OUT_OF_RANGE_MESSAGE)
  })

  it('rejects non-numeric input', () => {
    const result = parseBookingHorizonInput('abc')
    expect(result.error).toBe(HORIZON_OUT_OF_RANGE_MESSAGE)
  })

  it('rejects non-integer input', () => {
    const result = parseBookingHorizonInput('90.5')
    expect(result.error).toBe(HORIZON_OUT_OF_RANGE_MESSAGE)
  })
})

describe('parseHorizonExceededDays', () => {
  it('extracts N from the exact server wording', () => {
    expect(parseHorizonExceededDays('Записаться можно не дальше чем на 7 дней вперёд')).toBe(7)
  })

  it('extracts N regardless of surrounding text', () => {
    expect(parseHorizonExceededDays('на 365 дней вперёд')).toBe(365)
  })

  it('returns null for unrelated 400 messages', () => {
    expect(parseHorizonExceededDays('Мастер не оказывает услугу: Стрижка')).toBeNull()
    expect(parseHorizonExceededDays('')).toBeNull()
  })
})

describe('lastBookableDateUtc', () => {
  it('measures the horizon from the UTC date, not the browser-local one (UTC+7 after local midnight)', () => {
    // 2026-09-30 20:00 UTC is already 2026-10-01 03:00 in UTC+7. The server still has "today" = 30 Sep,
    // so with a 30-day horizon its last bookable date is 30 Oct, not 31 Oct.
    expect(lastBookableDateUtc(new Date('2026-09-30T20:00:00Z'), 30)).toBe('2026-10-30')
  })

  it('rolls over month and year boundaries', () => {
    expect(lastBookableDateUtc(new Date('2026-12-20T12:00:00Z'), 30)).toBe('2027-01-19')
    expect(lastBookableDateUtc(new Date('2026-01-31T23:59:00Z'), 1)).toBe('2026-02-01')
  })
})
