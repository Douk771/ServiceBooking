// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { formatMonthlyPrice, formatIncludedLimit, formatIncludedLimitLine, pickFeaturedPlanId } from './pricingFormat'

describe('formatMonthlyPrice', () => {
  it('zero → "Бесплатно"', () => {
    expect(formatMonthlyPrice(0)).toBe('Бесплатно')
  })

  it('formats with thousands separator and the ru unit suffix', () => {
    // toLocaleString('ru-RU') uses a non-breaking space as the thousands separator.
    expect(formatMonthlyPrice(1490)).toBe('1 490 ₽/мес')
  })

  it('does not append a bare number without a unit (US-71 п. 3)', () => {
    expect(formatMonthlyPrice(690)).not.toMatch(/^\d+$/)
    expect(formatMonthlyPrice(690)).toContain('₽/мес')
  })
})

describe('formatIncludedLimit', () => {
  it('groups thousands (T37-12)', () => {
    expect(formatIncludedLimit(1500, 'заказа', 'заказов').replace(/\s/g, ' ')).toBe('до 1 500 заказов')
  })
  it('null → "без ограничений"', () => {
    expect(formatIncludedLimit(null, 'компании', 'компаний')).toBe('без ограничений')
  })

  it('1 → genitive singular ("до 1 компании", not nominative "компания")', () => {
    expect(formatIncludedLimit(1, 'компании', 'компаний')).toBe('до 1 компании')
  })

  it('5 → genitive plural', () => {
    expect(formatIncludedLimit(5, 'сотрудника', 'сотрудников')).toBe('до 5 сотрудников')
  })

  it('21 → genitive singular (ends in 1, like 1, unlike 11)', () => {
    expect(formatIncludedLimit(21, 'компании', 'компаний')).toBe('до 21 компании')
  })

  it('11 → genitive plural (the *11 exception)', () => {
    expect(formatIncludedLimit(11, 'компании', 'компаний')).toBe('до 11 компаний')
  })

  it('2..4 → genitive plural too (unlike nominative "few" form)', () => {
    expect(formatIncludedLimit(3, 'компании', 'компаний')).toBe('до 3 компаний')
  })
})

describe('formatIncludedLimitLine (cycle 28, «Сеть»: null limits)', () => {
  it('null names what is unlimited instead of a bare «без ограничений»', () => {
    expect(formatIncludedLimitLine(null, 'компании', 'компании', 'компаний')).toBe('Компании без ограничений')
    expect(formatIncludedLimitLine(null, 'сотрудники', 'сотрудника', 'сотрудников')).toBe('Сотрудники без ограничений')
  })

  it('a number reads as formatIncludedLimit does', () => {
    expect(formatIncludedLimitLine(3, 'компании', 'компании', 'компаний')).toBe('до 3 компаний')
    expect(formatIncludedLimitLine(1, 'компании', 'компании', 'компаний')).toBe('до 1 компании')
  })
})

describe('pickFeaturedPlanId (cycle 28: the trial sits right after the free plan)', () => {
  const plan = (id: string, sortOrder: number, o: { isFree?: boolean; isTrial?: boolean; price?: number } = {}) => ({
    id,
    sortOrder,
    isFree: o.isFree ?? false,
    isTrial: o.isTrial,
    pricePerMonth: o.price ?? 1000,
  })

  it('skips the free plan AND the trial: the accent goes to the first plan that is actually paid', () => {
    const plans = [
      plan('network', 40, { price: 3900 }),
      plan('trial', 10, { isTrial: true, price: 0 }),
      plan('studio', 20, { price: 790 }),
      plan('free', -1, { isFree: true, price: 0 }),
      plan('salon', 30, { price: 1890 }),
    ]
    expect(pickFeaturedPlanId(plans)).toBe('studio')
  })

  it('no paid plans → nothing is accented', () => {
    expect(pickFeaturedPlanId([plan('free', -1, { isFree: true, price: 0 }), plan('trial', 10, { isTrial: true, price: 0 })])).toBeUndefined()
  })
})
