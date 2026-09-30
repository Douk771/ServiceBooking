// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { formatRub, formatRubRounded } from './money'

describe('formatRub', () => {
  it('is exactly the `${x.toLocaleString("ru-RU")} ₽` template the pages used inline', () => {
    expect(formatRub(12345)).toBe(`${(12345).toLocaleString('ru-RU')} ₽`)
    expect(formatRub(0)).toBe('0 ₽')
  })

  it('groups thousands with a non-breaking space and keeps kopecks', () => {
    expect(formatRub(1500)).toBe('1\u00a0500 ₽')
    expect(formatRub(99.5)).toBe('99,5 ₽')
  })
})

// Moved from pages/admin/billingAccountsHelpers.test.ts together with the function (cycle 22).
describe('formatRubRounded', () => {
  it('formats whole rubles with a ru-RU thousands separator and no kopecks', () => {
    expect(formatRubRounded(12345)).toBe(`${(12345).toLocaleString('ru-RU')} ₽`)
  })

  it('rounds fractional values to the nearest ruble', () => {
    expect(formatRubRounded(999.6)).toBe(`${(1000).toLocaleString('ru-RU')} ₽`)
  })
})
