import { describe, it, expect } from 'vitest'
import { formatUtcOffset, formatCityTimeZone, utcOffsetMinutesOf } from './timezone'

describe('formatUtcOffset', () => {
  it('formats a positive whole-hour offset', () => {
    expect(formatUtcOffset(420)).toBe('UTC+7')
  })

  it('formats a negative offset', () => {
    // Kaliningrad, UTC+2 stored as +120 normally, but exercise the negative branch explicitly
    expect(formatUtcOffset(-300)).toBe('UTC-5')
  })

  it('formats a half-hour offset', () => {
    expect(formatUtcOffset(330)).toBe('UTC+5:30')
  })

  it('formats zero offset', () => {
    expect(formatUtcOffset(0)).toBe('UTC+0')
  })
})

describe('formatCityTimeZone', () => {
  it('Barnaul is UTC+7 with its own IANA zone — not Novosibirsk, despite matching offsets', () => {
    expect(formatCityTimeZone('Барнаул, Алтайский край', 420, 'Asia/Barnaul')).toBe(
      'Барнаул, Алтайский край → UTC+7, Asia/Barnaul',
    )
  })
})

describe('utcOffsetMinutesOf', () => {
  it.each([
    ['Asia/Barnaul', 420],
    ['Europe/Moscow', 180],
    ['Asia/Kolkata', 330],
    ['UTC', 0],
  ])('%s -> %i', (zone, expected) => {
    expect(utcOffsetMinutesOf(zone)).toBe(expected)
  })

  it('returns null for an unknown zone', () => {
    expect(utcOffsetMinutesOf('Mars/Olympus')).toBeNull()
  })
})
