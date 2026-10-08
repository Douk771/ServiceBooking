import { describe, it, expect } from 'vitest'
import { staysPlanLimitError, staysPlanPayload } from '../pages/admin/planForm'
import { COMPANY_KIND_FILTERS, companyKindLabel, kindParam } from './companyKind'

describe('company kind «Дома» (cycle 37, §37.3.3 п. 4)', () => {
  it('has its own label and its own admin filter, sent as ?kind=Stays', () => {
    expect(companyKindLabel('Stays')).toBe('Дома')
    expect(companyKindLabel('Orders')).toBe('Магазин')
    expect(companyKindLabel(undefined)).toBe('Салон')
    expect(COMPANY_KIND_FILTERS.map((f) => f.label)).toEqual(['Все', 'Салоны', 'Магазины', 'Дома'])
    expect(kindParam('Stays')).toBe('Stays')
    expect(kindParam('all')).toBeUndefined()
  })
})

describe('plan form «Дома»', () => {
  it('sends maxHouses (empty = no limit); a salon plan sends nothing new', () => {
    expect(staysPlanPayload({ line: 'Stays', maxHouses: '5' }, false)).toEqual({ line: 'Stays', maxHouses: 5 })
    expect(staysPlanPayload({ line: 'Stays', maxHouses: ' ' }, true)).toEqual({ maxHouses: null })
    expect(staysPlanPayload({ line: 'Services', maxHouses: '5' }, false)).toEqual({})
    expect(staysPlanLimitError({ line: 'Stays', maxHouses: 'abc' })).toMatch(/целое число/)
    expect(staysPlanLimitError({ line: 'Orders', maxHouses: 'abc' })).toBeNull()
  })
})
