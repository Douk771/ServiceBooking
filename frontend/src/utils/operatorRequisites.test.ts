// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { fillOperatorTemplate, OPERATOR, OperatorTemplateError, resolveStorefrontHref, telHref } from './operatorRequisites'
import { PRODUCTION_SITE_URLS } from './publicSites'
import { STOREFRONT_PAGES } from './storefrontMarkup'

// CY48-06 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1): the template filler, tel: href and the per-site /pricing link.
describe('fillOperatorTemplate', () => {
  it('fills every placeholder declared in storefront-pages.json', () => {
    for (const key of STOREFRONT_PAGES.placeholders) {
      const filled = fillOperatorTemplate(`[{{${key}}}]`)
      expect(filled, key).not.toContain('{{')
      expect(filled.length, key).toBeGreaterThan(2)
    }
  })

  it('substitutes values and the derived phone key', () => {
    expect(fillOperatorTemplate('ИНН {{ИНН_ОПЕРАТОРА}}, {{ТЕЛЕФОН_TEL}}')).toBe(`ИНН ${OPERATOR['ИНН_ОПЕРАТОРА']}, ${telHref().slice('tel:'.length)}`)
  })

  it('throws on an unknown key instead of printing an empty string', () => {
    expect(() => fillOperatorTemplate('{{НЕТ_ТАКОГО_КЛЮЧА}}')).toThrow(OperatorTemplateError)
    expect(() => fillOperatorTemplate('{{}}')).toThrow(OperatorTemplateError)
  })

  it('leaves text without placeholders unchanged', () => {
    expect(fillOperatorTemplate('Оплата и возврат')).toBe('Оплата и возврат')
  })
})

describe('telHref', () => {
  it('is tel:+7 and ten digits, derived from the visible phone', () => {
    expect(telHref()).toMatch(/^tel:\+7\d{10}$/)
    expect(telHref().slice('tel:+'.length)).toBe(OPERATOR['ТЕЛЕФОН_ОПЕРАТОРА'].replace(/\D/g, ''))
  })
})

describe('resolveStorefrontHref', () => {
  it.each([
    ['dom', '/pricing', 'https://ezbook.ru/pricing'],
    ['bani', '/pricing', 'https://ezbook.ru/pricing'],
    ['zapis', '/pricing', '/pricing'],
    ['goods', '/pricing', '/pricing'],
    ['dom', '/contacts', '/contacts'],
    ['bani', '/payment-info', '/payment-info'],
  ] as const)('%s %s -> %s', (site, href, expected) => {
    expect(resolveStorefrontHref(site, href, PRODUCTION_SITE_URLS)).toBe(expected)
  })
})
