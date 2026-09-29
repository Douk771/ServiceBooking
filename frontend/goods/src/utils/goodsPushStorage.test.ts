import { describe, it, expect } from 'vitest'
import { ORDER_PUSH_STORAGE_TTL_MS, orderPushStorageKey, orderPushStoredAtKey, pruneOrderPushStorage } from './goodsPush'

function fakeStorage(init: Record<string, string>) {
  const m = new Map(Object.entries(init))
  return {
    get length() { return m.size },
    key: (i: number) => Array.from(m.keys())[i] ?? null,
    getItem: (k: string) => m.get(k) ?? null,
    removeItem: (k: string) => void m.delete(k),
    keys: () => Array.from(m.keys()),
  }
}

describe('pruneOrderPushStorage', () => {
  const now = 10_000_000_000
  it('keeps fresh entries, drops expired and legacy (no timestamp) ones, ignores other keys', () => {
    const s = fakeStorage({
      [orderPushStorageKey('fresh')]: 'e1',
      [orderPushStoredAtKey('fresh')]: String(now - 1000),
      [orderPushStorageKey('old')]: 'e2',
      [orderPushStoredAtKey('old')]: String(now - ORDER_PUSH_STORAGE_TTL_MS - 1),
      [orderPushStorageKey('legacy')]: 'e3',
      other: 'x',
    })
    pruneOrderPushStorage(s, now)
    expect(s.keys().sort()).toEqual([orderPushStorageKey('fresh'), orderPushStoredAtKey('fresh'), 'other'].sort())
  })
})
