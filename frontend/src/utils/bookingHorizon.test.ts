import { describe, it, expect } from 'vitest'
import { parseBookingHorizonInput, HORIZON_OUT_OF_RANGE_MESSAGE } from './bookingHorizon'

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
