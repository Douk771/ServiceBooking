// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import { clearUserCache } from './clearUserCache'

describe('clearUserCache', () => {
  it('removes user queries but keeps demo-status', () => {
    const qc = new QueryClient()
    qc.setQueryData(['demo-status'], { resetting: false })
    qc.setQueryData(['my-bookings'], [1])
    qc.setQueryData(['company', 5], { id: 5 })

    clearUserCache(qc)

    expect(qc.getQueryData(['demo-status'])).toEqual({ resetting: false })
    expect(qc.getQueryData(['my-bookings'])).toBeUndefined()
    expect(qc.getQueryData(['company', 5])).toBeUndefined()
  })
})
