import { describe, it, expect } from 'vitest'
import { AxiosHeaders } from 'axios'
import { DEMO_RESTRICTED_FALLBACK, getDemoRestrictedMessage, isDemoResetting } from './demoHeaders'

const res = (status: number, data: unknown, headers: unknown) => ({ response: { status, data, headers } })

describe('getDemoRestrictedMessage (API_CONTRACT_CYCLE28.md §599)', () => {
  it('403 + X-Demo-Restricted → the body text', () => {
    expect(
      getDemoRestrictedMessage(res(403, 'В демо-версии это действие недоступно.', { 'x-demo-restricted': '1' })),
    ).toBe('В демо-версии это действие недоступно.')
  })

  it('header lookup is case-insensitive and works with AxiosHeaders', () => {
    expect(getDemoRestrictedMessage(res(403, 'Нельзя', { 'X-Demo-Restricted': '1' }))).toBe('Нельзя')
    expect(getDemoRestrictedMessage(res(403, 'Нельзя', new AxiosHeaders({ 'X-Demo-Restricted': '1' })))).toBe('Нельзя')
  })

  it('header present but body empty → the contract text, not an empty error', () => {
    expect(getDemoRestrictedMessage(res(403, '', { 'x-demo-restricted': '1' }))).toBe(DEMO_RESTRICTED_FALLBACK)
    expect(getDemoRestrictedMessage(res(403, { unexpected: true }, { 'x-demo-restricted': '1' }))).toBe(
      DEMO_RESTRICTED_FALLBACK,
    )
  })

  it('a 403 without the header is NOT a demo restriction, whatever the body says', () => {
    expect(getDemoRestrictedMessage(res(403, 'В демо-версии это действие недоступно.', {}))).toBeNull()
    expect(getDemoRestrictedMessage(res(403, '', undefined))).toBeNull()
  })

  it('other statuses and non-axios errors are not a demo restriction', () => {
    expect(getDemoRestrictedMessage(res(401, 'x', { 'x-demo-restricted': '1' }))).toBeNull()
    expect(getDemoRestrictedMessage(new Error('boom'))).toBeNull()
    expect(getDemoRestrictedMessage(undefined)).toBeNull()
  })
})

describe('isDemoResetting (API_CONTRACT_CYCLE28.md §600a)', () => {
  it('503 + X-Demo-Resetting → true', () => {
    expect(isDemoResetting(res(503, 'Демо обновляется, зайдите через минуту', { 'x-demo-resetting': '1' }))).toBe(true)
  })

  it('an ordinary 503 (outage) is not a reset', () => {
    expect(isDemoResetting(res(503, '', {}))).toBe(false)
    expect(isDemoResetting(res(503, '', { 'retry-after': '60' }))).toBe(false)
  })

  it('the header on another status does not count', () => {
    expect(isDemoResetting(res(500, '', { 'x-demo-resetting': '1' }))).toBe(false)
    expect(isDemoResetting(new Error('boom'))).toBe(false)
  })
})
