// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { demoRoleHome } from './demoRoles'

describe('demoRoleHome (API_CONTRACT_CYCLE35.md §35.22)', () => {
  it('sends each salon demo role to its own start screen', () => {
    expect(demoRoleHome('owner')).toBe('/cabinet')
    expect(demoRoleHome('master')).toBe('/my-bookings')
    expect(demoRoleHome('client')).toBe('/my-visits')
  })

  it('sends the shop owner and staff to the goods cabinet, the customer to «Мои заказы»', () => {
    expect(demoRoleHome('shop-owner')).toBe('/cabinet')
    expect(demoRoleHome('shop-staff')).toBe('/cabinet')
    expect(demoRoleHome('shop-customer')).toBe('/orders')
  })
})
