import { describe, it, expect } from 'vitest'
import { formatMonthlyPrice, formatIncludedLimit, pluralizeRu } from './pricingFormat'

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
  it('null → "без ограничений"', () => {
    expect(formatIncludedLimit(null, 'компания', 'компании', 'компаний')).toBe('без ограничений')
  })

  it('1 → singular form', () => {
    expect(formatIncludedLimit(1, 'компания', 'компании', 'компаний')).toBe('до 1 компания')
  })

  it('5 → "many" form', () => {
    expect(formatIncludedLimit(5, 'сотрудник', 'сотрудника', 'сотрудников')).toBe('до 5 сотрудников')
  })
})

describe('pluralizeRu', () => {
  it.each([
    [1, 'компания'],
    [21, 'компания'],
    [2, 'компании'],
    [3, 'компании'],
    [24, 'компании'],
    [5, 'компаний'],
    [11, 'компаний'],
    [12, 'компаний'],
    [0, 'компаний'],
  ])('%i → %s', (count, expected) => {
    expect(pluralizeRu(count, 'компания', 'компании', 'компаний')).toBe(expected)
  })
})
