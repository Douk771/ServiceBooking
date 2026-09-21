import { describe, it, expect } from 'vitest'
import { isPlausibleInn } from './inn'

describe('isPlausibleInn', () => {
  it('accepts a 10-digit INN (legal entity)', () => {
    expect(isPlausibleInn('7707083893')).toBe(true)
  })

  it('accepts a 12-digit INN (individual/IP)', () => {
    expect(isPlausibleInn('770708389300')).toBe(true)
  })

  it('ignores non-digit formatting characters', () => {
    expect(isPlausibleInn('77-07-08-38-93')).toBe(true)
  })

  it('rejects a wrong length', () => {
    expect(isPlausibleInn('123')).toBe(false)
    expect(isPlausibleInn('12345678901')).toBe(false)
  })

  it('rejects an empty value', () => {
    expect(isPlausibleInn('')).toBe(false)
  })
})
