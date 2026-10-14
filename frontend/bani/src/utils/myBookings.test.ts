import { describe, it, expect } from 'vitest'
import { orderPathOf } from './myBookings'

describe('orderPathOf', () => {
  it('accepts only a relative /s/<token>', () => {
    expect(orderPathOf('/s/abc123')).toBe('/s/abc123')
    for (const bad of ['', 'https://evil.example/s/abc', '//evil.example/s/a', '/s/', '/s/a/b', '/s/a?x=1', '/cabinet']) expect(orderPathOf(bad), bad).toBeNull()
  })
})
