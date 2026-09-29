import { describe, it, expect } from 'vitest'
import { defaultForm, ordersPlanPayload, ordersPlanLimitsError } from './planForm'
import { buildAssignInput } from './billingAccountsHelpers'

describe('ordersPlanPayload (cycle 24)', () => {
  it('adds nothing for a salon plan, so its request body is exactly what it was', () => {
    expect(ordersPlanPayload({ ...defaultForm, line: 'Services', maxProductsPerShop: '50', maxOrdersPerMonth: '150' }, false)).toEqual({})
  })
  it('sends line only on creation (it cannot be changed later) and treats empty limits as «без ограничения»', () => {
    const f = { ...defaultForm, line: 'Orders' as const, maxProductsPerShop: '50', maxOrdersPerMonth: '', allowOrders: true }
    expect(ordersPlanPayload(f, false)).toEqual({ line: 'Orders', maxProductsPerShop: 50, maxOrdersPerMonth: null, allowOrders: true })
    expect(ordersPlanPayload(f, true)).toEqual({ maxProductsPerShop: 50, maxOrdersPerMonth: null, allowOrders: true })
  })
})

describe('ordersPlanLimitsError', () => {
  const o = { ...defaultForm, line: 'Orders' as const }
  it('accepts empty and whole numbers', () => {
    expect(ordersPlanLimitsError({ ...o, maxProductsPerShop: '', maxOrdersPerMonth: '150' })).toBeNull()
  })
  it('rejects typos instead of letting NaN become «без ограничения»', () => {
    expect(ordersPlanLimitsError({ ...o, maxOrdersPerMonth: '1о0' })).not.toBeNull()
    expect(ordersPlanLimitsError({ ...o, maxProductsPerShop: '-5' })).not.toBeNull()
  })
  it('ignores salon plans', () => {
    expect(ordersPlanLimitsError({ ...defaultForm, line: 'Services', maxOrdersPerMonth: 'abc' })).toBeNull()
  })
})

describe('buildAssignInput — line (cycle 24)', () => {
  const base = { planId: 'p1', isActive: true, paidUntil: '2026-10-31', rows: [], amount: '', comment: '', confirmLimitOverflow: false }
  it('does not mention the line for salons', () => {
    expect('line' in buildAssignInput(base)).toBe(false)
    expect('line' in buildAssignInput({ ...base, line: 'Services' })).toBe(false)
  })
  it('sends line: Orders when assigning the «Заказы» subscription', () => {
    expect(buildAssignInput({ ...base, line: 'Orders' }).line).toBe('Orders')
  })
})
