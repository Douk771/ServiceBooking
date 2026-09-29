import { describe, it, expect } from 'vitest'
import { newIdempotencyKey, orderPath } from './idempotency'

describe('newIdempotencyKey', () => {
  it('is a uuid and differs between calls', () => {
    const a = newIdempotencyKey()
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/)
    expect(newIdempotencyKey()).not.toBe(a)
  })
})

describe('orderPath', () => {
  it('uses the path of a same-origin orderUrl', () => {
    expect(orderPath(`${window.location.origin}/o/abc_DEF-1`, 'zzz')).toBe('/o/abc_DEF-1')
  })
  it('does not follow a foreign origin or a non-order path', () => {
    expect(orderPath('https://evil.example/o/abc', 'tok')).toBe('/o/tok')
    expect(orderPath(`${window.location.origin}/login`, 'tok')).toBe('/o/tok')
    expect(orderPath(null, 'a b')).toBe('/o/a%20b')
  })
})
