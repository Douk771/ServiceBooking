// @vitest-environment node
import { describe, expect, it } from 'vitest'
import storefrontPages from '../../../contracts/cycle48/storefront-pages.json'
import zapisLandingJson from '../../../contracts/cycle46/zapis-landing.json'
import { forbiddenRegexes, publicCopyViolations, visibleStrings } from '@/utils/forbiddenPhrases'
import { zapisLanding } from '@/pages/home/zapisLanding'
import { zapisFaq } from '@/pages/home/zapisFaq'

// CY48-07 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1, SPEC US-48-04): nothing a guest reads on a public page contains the phrases of
// contracts/cycle48/forbidden-phrases.json (scope `publicCopy`). ezbook (src/) must not import goods/dom/bani, so their configs are
// checked by forbiddenPhrases.guard.test.ts next to them (goods/src/landing, dom/src/landing, bani/src/landing).
const sources: Record<string, unknown> = {
  zapisLanding,
  zapisFaq,
  'storefront-pages.json': storefrontPages,
  'zapis-landing.json': zapisLandingJson,
}

describe('CY48-07 forbidden phrases in public copy (ezbook)', () => {
  it('the scope has the patterns of SPEC US-48-04', () => {
    expect(forbiddenRegexes('publicCopy').map((r) => r.id)).toEqual(
      expect.arrayContaining([
        'after-request',
        'we-will-contact',
        'depends-on-count',
        'by-count',
        'leftover-placeholder',
      ]),
    )
  })

  it.each(Object.keys(sources))('%s has no forbidden phrase', (name) => {
    expect(visibleStrings(sources[name]).length).toBeGreaterThan(3)
    expect(publicCopyViolations(sources[name])).toEqual([])
  })

  it('the patterns do catch the old texts and let the new ones pass (the guard is not blind)', () => {
    const regexes = forbiddenRegexes('publicCopy')
    const hit = (s: string) => regexes.some((r) => r.re.test(s))
    expect(hit('Итоговая цена — после заявки, по числу филиалов')).toBe(true)
    expect(hit('Чтобы сменить тариф, оставьте заявку в кабинете, и мы свяжемся с вами.')).toBe(true)
    expect(hit('Цена не зависит от числа филиалов')).toBe(false)
    expect(hit('Цена не зависит от числа гостей')).toBe(false)
    expect(hit('{{СРОК_СЧЁТА}}')).toBe(true)
  })
})
