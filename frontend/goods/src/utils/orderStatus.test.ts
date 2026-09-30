// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { formatAgo, isFailedOutcome, isTerminalStatus, minutesAgo } from './orderStatus'

describe('order status helpers', () => {
  it('knows the terminal statuses', () => {
    for (const s of ['Issued', 'Rejected', 'CancelledByCustomer', 'CancelledByShop', 'NotPickedUp'] as const) expect(isTerminalStatus(s)).toBe(true)
    for (const s of ['New', 'Accepted', 'Ready'] as const) expect(isTerminalStatus(s)).toBe(false)
  })
  it('«Выдан» is terminal but not a failed outcome', () => {
    expect(isFailedOutcome('Issued')).toBe(false)
    expect(isFailedOutcome('Rejected')).toBe(true)
    expect(isFailedOutcome('Ready')).toBe(false)
  })
  it('measures age against the server clock', () => {
    expect(minutesAgo('2026-10-05T10:00:00Z', '2026-10-05T10:12:30Z')).toBe(12)
    expect(minutesAgo('2026-10-05T10:00:00Z', '2026-10-05T09:59:00Z')).toBe(0)
    expect(minutesAgo('2026-10-05T10:00:00Z', new Date('2026-10-05T10:05:00Z').getTime())).toBe(5)
  })
  it('formats age', () => {
    expect(formatAgo(0)).toBe('только что')
    expect(formatAgo(7)).toBe('7 мин назад')
    expect(formatAgo(125)).toBe('2 ч 5 мин назад')
    expect(formatAgo(120)).toBe('2 ч назад')
    expect(formatAgo(60 * 50)).toBe('2 дн назад')
  })
})
