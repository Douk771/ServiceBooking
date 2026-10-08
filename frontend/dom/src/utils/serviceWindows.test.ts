// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { parseWindowTime, validateWindows, windowTimeValue } from './serviceWindows'

// Editor input: «02:00» in a window belongs to the NEXT calendar day, the end «06:00» closes the business day.
describe('parseWindowTime', () => {
  it.each([
    ['18:00', 'start', 1080],
    ['06:00', 'start', 360],
    ['02:00', 'end', 1560],
    ['02:00', 'start', 1560],
    ['06:00', 'end', 1800],
    ['00:00', 'end', 1440],
    ['05:59', 'end', 1799],
  ] as const)('%s as %s → %i', (text, role, expected) => {
    expect(parseWindowTime(text, role)).toBe(expected)
  })

  it('rejects malformed values', () => {
    expect(parseWindowTime('', 'start')).toBeNull()
    expect(parseWindowTime('24:00', 'end')).toBeNull()
    expect(parseWindowTime('7:5', 'end')).toBeNull()
  })

  it('round-trips with the time input value', () => {
    expect(windowTimeValue(1560)).toBe('02:00')
    expect(parseWindowTime(windowTimeValue(1560), 'end')).toBe(1560)
  })
})

describe('validateWindows — which windows overlap', () => {
  it('tells the editor the two offending windows', () => {
    const r = validateWindows([
      { startMinute: 900, endMinute: 1200 },
      { startMinute: 720, endMinute: 960 },
    ])
    expect(r).toMatchObject({ ok: false, error: 'WindowsOverlap', first: 1, second: 0 })
  })
})
