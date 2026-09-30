// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { demoRoleHome } from './demoRoles'

describe('demoRoleHome (API_CONTRACT_CYCLE28.md §598)', () => {
  it('sends each demo role to its own start screen', () => {
    expect(demoRoleHome('owner')).toBe('/cabinet')
    expect(demoRoleHome('master')).toBe('/my-bookings')
    expect(demoRoleHome('client')).toBe('/my-visits')
  })
})
