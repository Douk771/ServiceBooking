// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { BOOKING_PUSH_TTL_MS, bookingPushAtKey, bookingPushKey, guestPushMessage, pruneBookingPushStorage } from './stayPush'

function memoryStorage(initial: Record<string, string>) {
  const m = new Map(Object.entries(initial))
  return {
    get length() {
      return m.size
    },
    key: (i: number) => [...m.keys()][i] ?? null,
    getItem: (k: string) => m.get(k) ?? null,
    removeItem: (k: string) => void m.delete(k),
    keys: () => [...m.keys()],
  }
}

describe('guest push memory', () => {
  const now = 10_000_000_000_000

  it('keeps fresh entries, drops old ones and ones without a timestamp, leaves other keys alone', () => {
    const s = memoryStorage({
      [bookingPushKey('fresh')]: 'ep1',
      [bookingPushAtKey('fresh')]: String(now - 1000),
      [bookingPushKey('old')]: 'ep2',
      [bookingPushAtKey('old')]: String(now - BOOKING_PUSH_TTL_MS - 1),
      [bookingPushKey('legacy')]: 'ep3',
      unrelated: 'x',
    })
    pruneBookingPushStorage(s, now)
    expect(s.keys().sort()).toEqual([bookingPushAtKey('fresh'), bookingPushKey('fresh'), 'unrelated'].sort())
  })
})

describe('guest push reasons', () => {
  it('every reason has a text that points the guest back to the booking page', () => {
    for (const r of ['ios-safari-not-installed', 'ios-version-too-old', 'unsupported-browser', 'insecure-context', 'permission-denied', 'ios-permission-denied'] as const) {
      expect(guestPushMessage(r)).toContain('Сведения о брони всегда доступны на этой странице')
    }
    expect(guestPushMessage('company-disabled')).toBe('Компания отключила уведомления о бронях.')
  })
})
