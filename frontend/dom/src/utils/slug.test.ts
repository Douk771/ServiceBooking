// @vitest-environment node
import { describe, it, expect } from 'vitest'
import routes from '../../../../contracts/cycle37/dom-routes.json'
import { isCompanySlugValid, isHouseSlugValid, normalizeSlugInput, HOUSE_SLUG_FORMAT_TEXT } from './slug'

describe('addresses (dom-routes.json is the source)', () => {
  it('company slug: 3–50 chars, latin, digits, single hyphens', () => {
    expect(isCompanySlugValid('lesnoy-dom')).toBe(true)
    expect(isCompanySlugValid('ab')).toBe(false)
    expect(isCompanySlugValid('a'.repeat(51))).toBe(false)
    expect(isCompanySlugValid('-lesnoy')).toBe(false)
    expect(isCompanySlugValid('lesnoy--dom')).toBe(false)
    expect(isCompanySlugValid('Lesnoy')).toBe(false)
    expect(isCompanySlugValid('лесной')).toBe(false)
  })

  it('house slug: 2–50 chars', () => {
    expect(isHouseSlugValid('d1')).toBe(true)
    expect(isHouseSlugValid('d')).toBe(false)
    expect(isHouseSlugValid('a'.repeat(51))).toBe(false)
    expect(HOUSE_SLUG_FORMAT_TEXT).toBe('Адрес дома — латиница, цифры и дефис, 2–50 символов')
  })

  it('normalizes typed input', () => {
    // Cyrillic is dropped (the server transliterates names); what is left is the digit.
    expect(normalizeSlugInput('Лесной Дом 2')).toBe('2')
    expect(normalizeSlugInput('My  House_1')).toBe('my-house-1')
    expect(normalizeSlugInput('--abc')).toBe('abc')
  })

  it('the first segment of every route of the app is in the reserve, so an address can never shadow a route', () => {
    const reserved = new Set(routes.reservedSlugs)
    for (const path of routes.spaRoutes) {
      const seg = path.split('/').filter(Boolean)[0]
      if (seg) expect(reserved.has(seg), seg).toBe(true)
    }
  })
})
