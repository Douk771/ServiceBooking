// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { BATH_FALLBACKS } from './utils/baniTexts'
import { BANI_GUEST_WORDS } from './utils/baniGuestWords'
import { bathsVertical } from './vertical'

// FE-42-8 / LEGAL_REVIEW_CYCLE42.md §10 (Т42-11, ЮР39-8, US-42-22). The guard of dom (dom/src/guestCopy.guard.test.ts) scans only dom/src;
// the guest screens bani shows live partly in the shared `src/components/slots/{views,services,ui}`, so this one scans them too.
// Comments are stripped (the words are explained there); strings and markup are checked.
const sources = import.meta.glob(
  [
    './pages/*.tsx',
    './components/**/*.tsx',
    './utils/*.ts',
    './landing/*.ts',
    '../../src/components/slots/views/*.tsx',
    '../../src/components/slots/services/*.tsx',
    '../../src/components/slots/ui/*.tsx',
  ],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>

const SHARED = (p: string) => p.startsWith('../../src/')
// Staff / owner screens: the cabinet may say «бизнес-день» and the like, it is not guest copy (the forbidden-everywhere list still applies below).
// baniTexts.ts mixes guest and owner texts (owner ones may say «не возвращает», «заказ»): its guest keys are checked as objects below.
const OWNER_ONLY = /(baniTexts|\/Cabinet[A-Za-z]*View|cabinet|Cabinet|staff|Staff|Manual|ownerText|restrictedItems|myBookings|guestStorage|catalogQuery|baniSlug|guestsCount)/
const isTest = (p: string) => /\.test\.tsx?$/.test(p)

function stripComments(src: string): string {
  return src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1')
}

// ownerText.ts is the validator that refuses these words in the owner's text: it has to name them.
const RULE_FILES = /utils\/ownerText\.ts$/
const ALL = Object.entries(sources).filter(([p]) => !isTest(p))
const GUEST = ALL.filter(([p]) => !OWNER_ONLY.test(p))
// BathBookingTerms etc. legitimately say «кассовый чек» (a document about settlement, §10), so «чек» is checked only outside the texts file.
const NOT_TEXTS = (p: string) => !/baniTexts/.test(p)

const L = '(?<!\\p{L})'
const R = '(?!\\p{L})'

describe('guest-facing sources of bani', () => {
  it('scans a meaningful set of files, shared guest components included', () => {
    const names = GUEST.map(([p]) => p)
    expect(GUEST.length).toBeGreaterThan(15)
    expect(names.some((n) => SHARED(n) && n.endsWith('views/ServiceView.tsx'))).toBe(true)
    expect(names.some((n) => SHARED(n) && n.endsWith('views/ServiceOrderView.tsx'))).toBe(true)
    expect(names.some((n) => SHARED(n) && n.endsWith('services/ServiceOrderPanel.tsx'))).toBe(true)
    expect(names.some((n) => SHARED(n) && n.endsWith('ui/ProofUploader.tsx'))).toBe(true)
    expect(names.some((n) => n.endsWith('pages/CatalogPage.tsx'))).toBe(true)
    // the cabinet views and staff screens are not guest copy
    expect(names.some((n) => /Cabinet[A-Za-z]*View|\/staff\//.test(n))).toBe(false)
  })

  it.each([
    ['«бизнес-день»', /бизнес-?\s*д[её]н/i],
    ['«часы 6…30»', /часы\s*6\s*(…|\.\.\.|–|-)\s*30/i],
    ['запись «25:00», «26:00»', /(?<!\d)(2[4-9]|3\d):00/],
    ['«задаток / невозвратный / депозит»', /задат|невозвратн|депозит/i],
    ['«штраф / неустойка / не возвращается / плата за неявку»', /штраф|неустойк|не\s+возвращает|за\s+неявку/i],
    ['«туристический налог»', /туристическ/i],
    ['«дом», «бронь дома»', new RegExp(`${L}дом(а|ов|у|е|ом|ам|ах)?${R}`, 'iu')],
    ['«проживание», «заселение», «заезд»', /проживани|заселени|заезд|заселя/i],
    ['«заказ»', new RegExp(`${L}заказ[а-яё]*${R}`, 'iu')],
  ])('has no %s in strings or markup', (_name, rx) => {
    const offenders = GUEST.filter(([, src]) => rx.test(stripComments(src))).map(([p]) => p)
    expect(offenders).toEqual([])
  })

  it('does not call a file of the guest «чек» (only «подтверждение оплаты»)', () => {
    const rx = new RegExp(`${L}чек(а|и|ом|у|ов)?${R}`, 'iu')
    const offenders = GUEST.filter(([p, src]) => NOT_TEXTS(p) && rx.test(stripComments(src))).map(([p]) => p)
    expect(offenders).toEqual([])
  })

  it('has no «задаток / невозвратный / депозит» anywhere in bani/src, the cabinet and shared slot screens included', () => {
    const offenders = ALL.filter(([p, src]) => !RULE_FILES.test(p) && /задат|невозвратн|депозит/i.test(stripComments(src))).map(([p]) => p)
    expect(offenders).toEqual([])
  })
})

describe('texts of bani in objects', () => {
  const texts = JSON.stringify(BATH_FALLBACKS)
  const guestKeys = (Object.keys(BATH_FALLBACKS) as (keyof typeof BATH_FALLBACKS)[]).filter((k) => !/Owner/.test(k))

  it.each(guestKeys)('guest text %s has no forbidden words', (key) => {
    const t = JSON.stringify(BATH_FALLBACKS[key])
    for (const rx of [
      /бизнес-?\s*д[её]н/i,
      /задат|невозвратн|депозит/i,
      /штраф|неустойк|за\s+неявку|не\s+возвращает/i,
      /туристическ/i,
      /проживани|заселени|заезд/i,
      new RegExp(`${L}дом(а|ов|у|е|ом|ам|ах)?${R}`, 'iu'),
    ])
      expect(t).not.toMatch(rx)
  })

  it('no text of bani promises the forbidden', () => {
    expect(texts).not.toMatch(/задат|невозвратн|депозит|туристическ/i)
  })

  it('guest words and the words of the vertical say «бронь», never «заказ»', () => {
    const rx = new RegExp(`${L}заказ[а-яё]*${R}`, 'iu')
    expect(JSON.stringify(BANI_GUEST_WORDS)).not.toMatch(rx)
    expect(JSON.stringify({ ...bathsVertical.words, cabinet: bathsVertical.words.cabinet })).not.toMatch(rx)
  })

  it('the cabinet words say «бронь» and «ресурс», not «услуга»', () => {
    const w = JSON.stringify(bathsVertical.words.cabinet)
    expect(w).not.toMatch(/Ручной заказ|услуг/i)
    expect(w).toMatch(/Ручная бронь/)
  })
})
