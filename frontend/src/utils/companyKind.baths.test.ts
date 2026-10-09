import { describe, it, expect } from 'vitest'
import { bathsPlanLimitError, bathsPlanPayload } from '../pages/admin/planForm'
import { buildAssignInput } from '../pages/admin/billingAccountsHelpers'
import { COMPANY_KIND_FILTERS, companyKindLabel, kindParam } from './companyKind'
import { deviceSiteLabel, duplicateHint, staffPushSwitchLabel } from './staffPushTexts'

describe('company kind «Бани» (cycle 42, §42.12.3)', () => {
  it('has its own label and admin filter, sent as ?kind=Baths', () => {
    expect(companyKindLabel('Baths')).toBe('Бани')
    expect(COMPANY_KIND_FILTERS.map((f) => f.label)).toEqual(['Все', 'Салоны', 'Магазины', 'Дома', 'Бани'])
    expect(kindParam('Baths')).toBe('Baths')
  })
})

describe('plan form «Бани»', () => {
  it('sends maxResources (empty = no limit); other lines send nothing', () => {
    expect(bathsPlanPayload({ line: 'Baths', maxResources: '4' }, false)).toEqual({ line: 'Baths', maxResources: 4 })
    expect(bathsPlanPayload({ line: 'Baths', maxResources: ' ' }, true)).toEqual({ maxResources: null })
    expect(bathsPlanPayload({ line: 'Stays', maxResources: '4' }, false)).toEqual({})
  })
  it('rejects non-integers and zero, only for Baths', () => {
    expect(bathsPlanLimitError({ line: 'Baths', maxResources: 'abc' })).toMatch(/Лимит ресурсов/)
    expect(bathsPlanLimitError({ line: 'Baths', maxResources: '0' })).toMatch(/Лимит ресурсов/)
    expect(bathsPlanLimitError({ line: 'Baths', maxResources: '' })).toBeNull()
    expect(bathsPlanLimitError({ line: 'Orders', maxResources: 'abc' })).toBeNull()
  })
})

describe('assigning a subscription of the line', () => {
  it('carries the line for Baths and leaves the salon body untouched', () => {
    const base = { planId: 'p', isActive: true, paidUntil: null, rows: [], amount: '', comment: '', confirmLimitOverflow: false }
    expect(buildAssignInput({ ...base, line: 'Baths' }).line).toBe('Baths')
    expect(buildAssignInput({ ...base, line: 'Services' })).not.toHaveProperty('line')
    expect(buildAssignInput(base)).not.toHaveProperty('line')
  })
})

describe('push texts with the «Бани» site', () => {
  it('names the site and the bookings of baths', () => {
    expect(deviceSiteLabel('Baths')).toBe('через bani.ezbook.ru')
    expect(staffPushSwitchLabel({ hasServices: false, hasOrders: false, hasBaths: true })).toBe('Уведомлять меня о новых бронях бань на этом устройстве')
    expect(staffPushSwitchLabel({ hasServices: true, hasOrders: false, hasStays: true, hasBaths: true })).toContain('записях, бронях домов и бронях бань')
    expect(duplicateHint({ site: 'Baths' } as never, false)).toContain('bani.ezbook.ru')
  })
})
