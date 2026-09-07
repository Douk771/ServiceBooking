import { describe, it, expect } from 'vitest'
import { formatPhone } from './phone'

describe('formatPhone', () => {
  it('formats an 11-digit Russian number starting with 7', () => {
    expect(formatPhone('79990000000')).toBe('+7 (999) 000-00-00')
  })

  it('formats a canonical number that already has no extra characters the same way', () => {
    expect(formatPhone('79161234567')).toBe('+7 (916) 123-45-67')
  })

  it('strips stray non-digit characters before formatting (defensive — server should already send canon)', () => {
    expect(formatPhone('+7 (999) 000-00-00')).toBe('+7 (999) 000-00-00')
  })

  it('falls back to +<digits> for a 10-digit number (not the 11-digit Russian shape)', () => {
    expect(formatPhone('9990000000')).toBe('+9990000000')
  })

  it('falls back to +<digits> for an international number', () => {
    expect(formatPhone('12125551234')).toBe('+12125551234')
  })

  it('falls back to +<digits> for an 11-digit number NOT starting with 7', () => {
    expect(formatPhone('89990000000')).toBe('+89990000000')
  })

  it('returns an empty string for an empty string', () => {
    expect(formatPhone('')).toBe('')
  })

  it('returns an empty string for null', () => {
    expect(formatPhone(null)).toBe('')
  })

  it('returns an empty string for undefined', () => {
    expect(formatPhone(undefined)).toBe('')
  })

  it('returns an empty string when the input has no digits at all', () => {
    expect(formatPhone('n/a')).toBe('')
  })
})
