import { describe, it, expect } from 'vitest'
import { revisionMoved, revisionQueryKeys, REVISION_POLL_MS } from './revision'

describe('revisionMoved', () => {
  it('treats the first answer as a baseline', () => {
    expect(revisionMoved(null, 5)).toBe(false)
    expect(revisionMoved(undefined, 5)).toBe(false)
  })
  it('is false for the same number and true when it moves in either direction', () => {
    expect(revisionMoved(5, 5)).toBe(false)
    expect(revisionMoved(5, 6)).toBe(true)
    expect(revisionMoved(6, 5)).toBe(true)
  })
  it('is false when the next answer is missing', () => {
    expect(revisionMoved(5, null)).toBe(false)
  })
})

describe('revisionQueryKeys', () => {
  it('covers the day, the list, the card, the schedule and the company counter of that company', () => {
    const keys = revisionQueryKeys('c1')
    expect(keys.map((k) => k[0])).toEqual(['stays-service-day', 'stays-service-sessions', 'stays-service-session', 'baths-schedule', 'baths-company'])
    expect(keys.every((k) => k[1] === 'c1')).toBe(true)
  })
  it('polls at most every 30 seconds', () => {
    expect(REVISION_POLL_MS).toBeLessThanOrEqual(30_000)
  })
})
