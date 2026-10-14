// @vitest-environment node
import { describe, it, expect } from 'vitest'
import type { ServiceDayBarDto } from '@/types/slots'
import { axisTicks, barBox, barTimeText, sortedBars } from './serviceDay'

const axis = { fromMinute: 1080, toMinute: 1560, midnightMinute: 1440 as const }

describe('barBox', () => {
  it('places a bar by its minutes on the axis', () => {
    expect(barBox(axis, 1320, 1440)).toEqual({ leftPct: 50, widthPct: 25, clippedLeft: false, clippedRight: false })
  })
  it('keeps a session across midnight as one bar', () => {
    const b = barBox(axis, 1380, 1500)
    expect(b.leftPct).toBeCloseTo(62.5)
    expect(b.widthPct).toBeCloseTo(25)
  })
  it('cuts a buffer that runs past the end of the axis and says so', () => {
    const b = barBox(axis, 1500, 1620)
    expect(b.clippedRight).toBe(true)
    expect(b.leftPct + b.widthPct).toBeCloseTo(100)
  })
  it('cuts a carry-over buffer that starts before the axis', () => {
    const b = barBox(axis, 1000, 1140)
    expect(b.clippedLeft).toBe(true)
    expect(b.leftPct).toBe(0)
    expect(b.widthPct).toBeCloseTo(12.5)
  })
})

describe('axisTicks', () => {
  it('marks the midnight', () => {
    const ticks = axisTicks(axis)
    expect(ticks[0]).toMatchObject({ minute: 1080, label: '18:00', leftPct: 0 })
    expect(ticks.find((t) => t.isMidnight)).toMatchObject({ minute: 1440, label: '00:00' })
    expect(ticks[ticks.length - 1].label).toBe('02:00')
  })
})

describe('sortedBars / barTimeText', () => {
  it('orders by start, a session before a buffer that starts together', () => {
    const mk = (kind: ServiceDayBarDto['kind'], s: number): ServiceDayBarDto => ({ kind, startMinute: s, endMinute: s + 60, label: kind, needsAction: false })
    expect(sortedBars([mk('Buffer', 1200), mk('Session', 1200), mk('Session', 1100)]).map((b) => `${b.kind}${b.startMinute}`)).toEqual(['Session1100', 'Session1200', 'Buffer1200'])
  })
  it('says «ночь» for after-midnight times in the list', () => {
    expect(barTimeText({ startMinute: 1380, endMinute: 1500 })).toBe('23:00 – 01:00 (ночь)')
  })
})
