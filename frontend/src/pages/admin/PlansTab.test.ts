import { describe, it, expect } from 'vitest'
import { optionRulesToForm, optionRulesToPayload, MAX_HIGHLIGHTS, PUBLIC_MAX_HIGHLIGHTS, MAX_HIGHLIGHT_LENGTH } from './PlansTab'

describe('optionRulesToForm', () => {
  it('maps each rule by optionId, converting includedQuantity to a string for the input', () => {
    const form = optionRulesToForm([
      { optionId: 'opt-1', availability: 'Included', includedQuantity: 2 },
      { optionId: 'opt-2', availability: 'Extra', includedQuantity: null },
    ])

    expect(form).toEqual({
      'opt-1': { availability: 'Included', includedQuantity: '2' },
      'opt-2': { availability: 'Extra', includedQuantity: '' },
    })
  })

  it('produces an empty map for a plan with no option rules — every option is implicitly Unavailable', () => {
    expect(optionRulesToForm([])).toEqual({})
  })
})

describe('optionRulesToPayload', () => {
  it('omits Unavailable rules — the contract treats a missing entry as Unavailable', () => {
    const payload = optionRulesToPayload({
      'opt-1': { availability: 'Included', includedQuantity: '3' },
      'opt-2': { availability: 'Unavailable', includedQuantity: '' },
    })

    expect(payload).toEqual([{ optionId: 'opt-1', availability: 'Included', includedQuantity: 3 }])
  })

  it('only sends includedQuantity for Included rules, never for Extra', () => {
    const payload = optionRulesToPayload({
      'opt-1': { availability: 'Extra', includedQuantity: '3' },
    })

    expect(payload).toEqual([{ optionId: 'opt-1', availability: 'Extra', includedQuantity: null }])
  })

  it('treats a blank includedQuantity on an Included rule as null, not NaN', () => {
    const payload = optionRulesToPayload({
      'opt-1': { availability: 'Included', includedQuantity: '' },
    })

    expect(payload).toEqual([{ optionId: 'opt-1', availability: 'Included', includedQuantity: null }])
  })
})

describe('MAX_HIGHLIGHTS', () => {
  it('matches the write cap the contract/backend enforce (AdminPlanDto.highlights maxItems)', () => {
    expect(MAX_HIGHLIGHTS).toBe(10)
  })
})

describe('PUBLIC_MAX_HIGHLIGHTS', () => {
  it('matches the showcase cap of five bullets (PricingPlanDto.highlights maxItems)', () => {
    expect(PUBLIC_MAX_HIGHLIGHTS).toBe(5)
  })
})

describe('MAX_HIGHLIGHT_LENGTH', () => {
  it('matches the per-item length cap the contract/backend enforce', () => {
    expect(MAX_HIGHLIGHT_LENGTH).toBe(120)
  })
})
