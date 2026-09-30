import { describe, it, expect } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useNoindexMeta } from './useNoindexMeta'

const robots = () => document.head.querySelectorAll('meta[name="robots"]')

describe('useNoindexMeta (API_CONTRACT_CYCLE28.md §591)', () => {
  it('adds noindex, nofollow while active and removes it on unmount', () => {
    const { unmount } = renderHook(() => useNoindexMeta(true))
    expect(robots()).toHaveLength(1)
    expect(robots()[0].getAttribute('content')).toBe('noindex, nofollow')
    unmount()
    expect(robots()).toHaveLength(0)
  })

  it('adds nothing for an ordinary company', () => {
    renderHook(() => useNoindexMeta(false))
    expect(robots()).toHaveLength(0)
  })

  it('follows the flag: a page that turns out not to be a showcase does not keep the tag', () => {
    const { rerender } = renderHook(({ on }) => useNoindexMeta(on), { initialProps: { on: true } })
    expect(robots()).toHaveLength(1)
    rerender({ on: false })
    expect(robots()).toHaveLength(0)
  })
})
