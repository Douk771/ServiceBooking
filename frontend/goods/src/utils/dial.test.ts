import { describe, it, expect } from 'vitest'
import { dialHref } from './dial'

describe('dialHref', () => {
  it('prefixes canonical digits with +', () => {
    expect(dialHref('79001234567')).toBe('tel:+79001234567')
  })
  it('normalizes a formatted number', () => {
    expect(dialHref('+7 (900) 123-45-67')).toBe('tel:+79001234567')
  })
  it('returns an empty string for nothing', () => {
    expect(dialHref(null)).toBe('')
    expect(dialHref('')).toBe('')
    expect(dialHref('abc')).toBe('')
  })
})
