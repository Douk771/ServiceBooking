import { describe, it, expect } from 'vitest'
import { formatPhone, maskPhoneInput, toCanonicalPhone, isRussianPhone, looksRussian, telHref } from './phone'

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

// ARCHITECTURE_CYCLE15.md §254 — the contacts block's clickable phone link.
describe('telHref', () => {
  it('turns a formatted display phone into a bare tel: URI', () => {
    expect(telHref('+7 (900) 000-00-00')).toBe('tel:+79000000000')
  })

  it('keeps a canonical digits-only number as-is with a tel: prefix', () => {
    expect(telHref('79990000000')).toBe('tel:79990000000')
  })

  it('preserves a leading + when present', () => {
    expect(telHref('+79990000000')).toBe('tel:+79990000000')
  })

  it('returns an empty string for empty/null/undefined', () => {
    expect(telHref('')).toBe('')
    expect(telHref(null)).toBe('')
    expect(telHref(undefined)).toBe('')
  })

  it('returns an empty string when there are no digits at all', () => {
    expect(telHref('n/a')).toBe('')
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

// US-61/US-60 fix (`SPEC_CYCLE6_BOOKING_FIXES.md` §0.1 Q8, ARCHITECTURE_CYCLE6.md §48.3/§48.4): the login form's mask must
// hold while the input still looks Russian and release once a different country code is decided —
// `looksRussian` is what `PhoneInput` uses to make that call on every keystroke. Deliberately NOT a
// second normalizer (that was the bug in the removed `toCanonicalPhoneLenient`, §48.3's "one function"
// rule) — it only classifies, the server (`PhoneNormalizer.Normalize`) does all the actual digit
// extraction for login.
describe('looksRussian', () => {
  it('treats a bare local number (no country code yet) as Russian-shaped', () => {
    expect(looksRussian('9990000000')).toBe(true)
  })

  it('treats a domestic 8-prefixed number as Russian-shaped', () => {
    expect(looksRussian('89990000000')).toBe(true)
  })

  it('treats a lone "+" with no digits yet as undecided (still Russian-shaped)', () => {
    expect(looksRussian('+')).toBe(true)
  })

  it('treats "+7…" as Russian-shaped', () => {
    expect(looksRussian('+79990000000')).toBe(true)
  })

  it('treats "+8…" as Russian-shaped (same dialing convention as domestic 8)', () => {
    expect(looksRussian('+89990000000')).toBe(true)
  })

  it('rejects a decided foreign country code', () => {
    expect(looksRussian('+380671234567')).toBe(false)
  })

  it('rejects as soon as the first foreign digit after "+" is typed, not just the full number', () => {
    expect(looksRussian('+3')).toBe(false)
  })

  it('treats empty input as Russian-shaped (nothing to reject yet)', () => {
    expect(looksRussian('')).toBe(true)
  })
})
