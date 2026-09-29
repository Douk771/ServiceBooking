import { describe, it, expect } from 'vitest'
import { companyKindLabel, isShop, kindParam } from './companyKind'

describe('companyKind', () => {
  it('labels kinds, treating an absent kind as a salon', () => {
    expect(companyKindLabel('Orders')).toBe('Магазин')
    expect(companyKindLabel('Services')).toBe('Салон')
    expect(companyKindLabel(undefined)).toBe('Салон')
  })
  it('sends no ?kind= for «Все»', () => {
    expect(kindParam('all')).toBeUndefined()
    expect(kindParam('Orders')).toBe('Orders')
    expect(kindParam('Services')).toBe('Services')
  })
  it('recognises a shop', () => {
    expect(isShop({ kind: 'Orders' })).toBe(true)
    expect(isShop({ kind: 'Services' })).toBe(false)
    expect(isShop({})).toBe(false)
  })
})
