import { describe, it, expect } from 'vitest'
import { limitTone, pauseRemainingText } from './acceptance'

const at = (iso: string) => new Date(iso).getTime()

describe('pauseRemainingText', () => {
  const paused = { mode: 'Paused' as const, pausedUntilUtc: '2026-09-30T10:30:00Z' }
  it('counts minutes up, so 1 second left still says «1 мин» rather than 0', () => {
    expect(pauseRemainingText(paused, at('2026-09-30T10:29:59Z'))).toBe('осталось 1 мин')
    expect(pauseRemainingText(paused, at('2026-09-30T10:15:00Z'))).toBe('осталось 15 мин')
  })
  it('switches to hours from 60 minutes', () => {
    expect(pauseRemainingText(paused, at('2026-09-30T09:30:00Z'))).toBe('осталось 1 ч')
    expect(pauseRemainingText(paused, at('2026-09-30T09:00:00Z'))).toBe('осталось 1 ч 30 мин')
  })
  it('is null when not paused, without an end, or already over', () => {
    expect(pauseRemainingText({ mode: 'Accepting', pausedUntilUtc: null }, at('2026-09-30T10:00:00Z'))).toBeNull()
    expect(pauseRemainingText({ mode: 'Paused', pausedUntilUtc: null }, at('2026-09-30T10:00:00Z'))).toBeNull()
    expect(pauseRemainingText(paused, at('2026-09-30T10:30:00Z'))).toBeNull()
  })
})

describe('limitTone', () => {
  it('maps the server warning level; unknown future levels are silent', () => {
    expect(limitTone('Warning80')).toBe('warning')
    expect(limitTone('Reached')).toBe('reached')
    expect(limitTone('None')).toBe('none')
    expect(limitTone('Something')).toBe('none')
  })
})
