import { describe, it, expect } from 'vitest'
import { CHECKLIST_TARGETS, checklistPath, checklistTitle, ownerStrip, planBannerTone } from './checklist'
import type { BathsCompanyManageDto } from './types'

const company = (over: Partial<BathsCompanyManageDto> = {}): BathsCompanyManageDto =>
  ({
    id: 'c1',
    gate: { accepting: true },
    plan: { isTrial: false, resourcesPublished: 0, warningLevel: 'None' },
    checklist: [],
    ...over,
  }) as BathsCompanyManageDto

describe('checklistPath', () => {
  it('opens the settings anchors for requisites and provider, the resources for publishing, the shared page for the plan', () => {
    expect(checklistPath('c1', 'ProfileFilled')).toBe('/cabinet/c1/settings')
    expect(checklistPath('c1', 'PaymentDetails')).toBe('/cabinet/c1/settings#payment-details')
    expect(checklistPath('c1', 'ProviderInfo')).toBe('/cabinet/c1/settings#provider')
    expect(checklistPath('c1', 'ResourcePublished')).toBe('/cabinet/c1/resources')
    expect(checklistPath('c1', 'Plan')).toBe('/cabinet/subscription')
  })

  it('knows all five steps of the contract, in its order', () => {
    expect(Object.keys(CHECKLIST_TARGETS)).toEqual(['ProfileFilled', 'PaymentDetails', 'ProviderInfo', 'ResourcePublished', 'Plan'])
  })
})

describe('checklistTitle', () => {
  it('says the guests cannot book only while the gate is shut', () => {
    expect(checklistTitle(false)).toBe('Гости не могут бронировать, потому что…')
    expect(checklistTitle(true)).toBe('Что осталось настроить')
  })
})

describe('planBannerTone', () => {
  it('is red for the shut states and amber for the early warnings', () => {
    expect(planBannerTone('Expired')).toBe('danger')
    expect(planBannerTone('NoPlan')).toBe('danger')
    expect(planBannerTone('OverLimit')).toBe('danger')
    expect(planBannerTone('TrialEnding3d')).toBe('warning')
    expect(planBannerTone('TrialEnding1d')).toBe('warning')
  })
})

describe('ownerStrip', () => {
  const pendingItem = { code: 'Plan' as const, done: false, text: 'Выберите тариф' }

  it('shows nothing when all is fine', () => {
    expect(ownerStrip(company(), true).visible).toBe(false)
  })

  it('lists only the unfinished steps, in the server order', () => {
    const c = company({ checklist: [{ code: 'ProfileFilled', done: true, text: 'a' }, pendingItem, { code: 'ProviderInfo', done: false, text: 'b' }] })
    expect(ownerStrip(c, true).pending.map((p) => p.code)).toEqual(['Plan', 'ProviderInfo'])
  })

  it('shows the reason of a shut gate and the server text of the plan', () => {
    const c = company({ gate: { accepting: false, reasonText: 'Нет тарифа' }, plan: { isTrial: false, resourcesPublished: 0, warningLevel: 'NoPlan', text: 'Тариф не выбран' } })
    const strip = ownerStrip(c, true)
    expect(strip.gate).toBe('Нет тарифа')
    expect(strip.plan).toEqual({ text: 'Тариф не выбран', tone: 'danger' })
    expect(strip.visible).toBe(true)
  })

  it('hides a warning level without a text', () => {
    expect(ownerStrip(company({ plan: { isTrial: true, resourcesPublished: 0, warningLevel: 'TrialEnding3d' } }), true).plan).toBeNull()
  })

  it('shows nothing to someone who cannot manage the company', () => {
    const c = company({ gate: { accepting: false, reasonText: 'x' }, checklist: [pendingItem], plan: { isTrial: false, resourcesPublished: 0, warningLevel: 'NoPlan', text: 't' } })
    expect(ownerStrip(c, false).visible).toBe(false)
  })
})
