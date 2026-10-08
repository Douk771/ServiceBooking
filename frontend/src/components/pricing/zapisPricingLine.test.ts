import { describe, it, expect } from 'vitest'
import { toZapisGridView } from './zapisPricingLine'
import type { PublicPricingDto } from '../../types/pricing'

describe('toZapisGridView', () => {
  it('builds limit lines for companies and employees, null = unlimited, defaults isTrial/legalNotice', () => {
    const dto: PublicPricingDto = {
      version: 'v', currency: 'RUB', notice: 'n', options: [],
      plans: [{ id: 'a', name: 'Сеть', description: null, pricePerMonth: 3900, highlights: ['h'], includedCompanies: null, includedEmployees: 21, sortOrder: 1, isFree: false }],
    }
    const v = toZapisGridView(dto)
    expect(v.plans[0].limitLines).toEqual(['Компании без ограничений', 'до 21 сотрудника'])
    expect(v.plans[0].isTrial).toBe(false)
    expect(v.legalNotice).toBeNull()
    expect(v.notice).toBe('n')
  })
})
