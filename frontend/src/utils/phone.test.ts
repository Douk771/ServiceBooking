import { describe, it, expect } from 'vitest'
import { formatPhone, maskPhoneInput, toCanonicalPhone, isRussianPhone, toCanonicalPhoneLenient } from './phone'

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

describe('toCanonicalPhone', () => {
  it('turns a leading 8 into 7', () => {
    expect(toCanonicalPhone('89990000000')).toBe('79990000000')
  })

  it('keeps a leading +7 as 7', () => {
    expect(toCanonicalPhone('+79990000000')).toBe('79990000000')
  })

  it('prepends 7 for a bare mobile number (no country code)', () => {
    expect(toCanonicalPhone('9990000000')).toBe('79990000000')
  })

  it('strips formatting characters from a fully-formatted paste', () => {
    expect(toCanonicalPhone('+7 (999) 000-00-00')).toBe('79990000000')
  })

  it('ignores digits typed beyond 11', () => {
    expect(toCanonicalPhone('7999000000099')).toBe('79990000000')
  })

  it('rejects an explicit foreign country code (+380…)', () => {
    expect(toCanonicalPhone('+380671234567')).toBe('')
  })

  it('returns an empty string for empty input', () => {
    expect(toCanonicalPhone('')).toBe('')
  })

  it('handles partial input while typing (fewer than 11 digits)', () => {
    expect(toCanonicalPhone('8999')).toBe('7999')
  })
})

describe('maskPhoneInput', () => {
  it('builds the mask progressively as digits are typed', () => {
    expect(maskPhoneInput('7')).toBe('+7')
    expect(maskPhoneInput('79')).toBe('+7 (9')
    expect(maskPhoneInput('7999')).toBe('+7 (999)')
    expect(maskPhoneInput('7999000')).toBe('+7 (999) 000')
    expect(maskPhoneInput('799900000')).toBe('+7 (999) 000-00')
    expect(maskPhoneInput('79990000000')).toBe('+7 (999) 000-00-00')
  })

  it('gives the same result for 8-prefixed and +7-prefixed full numbers', () => {
    expect(maskPhoneInput('89990000000')).toBe(maskPhoneInput('+79990000000'))
  })

  it('gives the same result for slitno digits and a formatted paste', () => {
    expect(maskPhoneInput('79990000000')).toBe(maskPhoneInput('+7 (999) 000-00-00'))
  })

  it('ignores stray junk characters mixed into the input', () => {
    expect(maskPhoneInput('+7 (999) --00 00-00 ')).toBe('+7 (999) 000-00-0')
  })

  it('returns an empty string for a rejected foreign number', () => {
    expect(maskPhoneInput('+380671234567')).toBe('')
  })

  it('returns an empty string for empty input', () => {
    expect(maskPhoneInput('')).toBe('')
  })
})

describe('isRussianPhone', () => {
  it('accepts an 11-digit number starting with 7', () => {
    expect(isRussianPhone('79990000000')).toBe(true)
  })

  it('rejects a 10-digit number', () => {
    expect(isRussianPhone('9990000000')).toBe(false)
  })

  it('rejects a 12-digit number', () => {
    expect(isRussianPhone('380671234567')).toBe(false)
  })

  it('rejects an 11-digit number not starting with 7', () => {
    expect(isRussianPhone('89990000000')).toBe(false)
  })
})

// US-63 fix (ARCHITECTURE_CYCLE6.md §947): the login form must accept a foreign number already on
// file, unlike toCanonicalPhone which is Russian-only (registration policy, §48.1/§48.2).
describe('toCanonicalPhoneLenient', () => {
  it('passes a foreign +<countrycode> number through untouched', () => {
    expect(toCanonicalPhoneLenient('+380671234567')).toBe('380671234567')
  })

  it('still folds a leading 8 into 7 for Russian-shaped input', () => {
    expect(toCanonicalPhoneLenient('89990000000')).toBe('79990000000')
  })

  it('still prefixes a bare local number with 7', () => {
    expect(toCanonicalPhoneLenient('9990000000')).toBe('79990000000')
  })

  it('caps at 15 digits (server E.164 bound)', () => {
    expect(toCanonicalPhoneLenient('+123456789012345678')).toBe('123456789012345')
  })

  it('returns an empty string for empty input', () => {
    expect(toCanonicalPhoneLenient('')).toBe('')
  })
})
