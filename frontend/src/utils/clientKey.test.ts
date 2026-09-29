import { describe, it, expect } from 'vitest'
import { getClientKey, healthConsentFormPrintPath } from './clientKey'

describe('getClientKey', () => {
  it('uses clientId when the client has an account', () => {
    expect(getClientKey({ clientId: 'u1', guestPhone: null })).toBe('u1')
  })

  it('falls back to the canonical phone key for a guest', () => {
    expect(getClientKey({ clientId: null, guestPhone: '79991234567' })).toBe('phone:79991234567')
  })
})

describe('healthConsentFormPrintPath', () => {
  it('builds the route for a registered client key', () => {
    expect(healthConsentFormPrintPath('co1', 'u1')).toBe('/companies/co1/clients/u1/health-consent-form')
  })

  it('URL-encodes a guest phone key, which otherwise contains a colon', () => {
    expect(healthConsentFormPrintPath('co1', 'phone:79991234567')).toBe('/companies/co1/clients/phone%3A79991234567/health-consent-form')
  })
})
