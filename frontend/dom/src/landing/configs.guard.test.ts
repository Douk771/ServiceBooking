import { describe, it, expect } from 'vitest'
import { stayFaq } from './stayFaq'
import { stayLanding } from './stayLanding'

// T41-03: конфиг главной «Домов» — только данные; страница — без собственной разметки секций (ARCHITECTURE_CYCLE41.md §41.10.1).
const raw = (m: Record<string, string>) => Object.entries(m)
const configs = raw(import.meta.glob(['./stayLanding.ts', './stayFaq.ts'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>)
const [[, pageSrc]] = raw(import.meta.glob('../pages/CatalogPage.tsx', { query: '?raw', import: 'default', eager: true }) as Record<string, string>)
const [[, appSrc]] = raw(import.meta.glob('../DomApp.tsx', { query: '?raw', import: 'default', eager: true }) as Record<string, string>)

const all = JSON.stringify({ stayLanding, stayFaq })
const business = JSON.stringify(stayLanding.business)
const L = '(?<!\\p{L})'
const R = '(?!\\p{L})'

describe('stay landing guard (T41-03)', () => {
  it('config files are .ts without className and JSX', () => {
    expect(configs).toHaveLength(2)
    for (const [file, src] of configs) {
      expect(file.endsWith('.ts')).toBe(true)
      expect(src).not.toContain('className')
      expect(src).not.toMatch(/<[A-Za-z]+[\s/>]/)
      expect(src).not.toContain('dangerouslySetInnerHTML')
    }
  })

  it('page has no own main/section/h1/h2', () => {
    expect(pageSrc).not.toMatch(/<main|<section|<h1|<h2/)
  })

  it('DomApp has exactly one footer and no main', () => {
    expect(appSrc.match(/<DomFooter \/>/g)).toHaveLength(1)
    expect(appSrc).not.toContain('<main')
  })

  it('FAQ has 6..8 items, none with a link', () => {
    expect(stayFaq.length).toBeGreaterThanOrEqual(6)
    expect(stayFaq.length).toBeLessThanOrEqual(8)
    expect(stayFaq.some((i) => i.link)).toBe(false)
  })

  it('has no pricing: no tariffs, no media, no prices', () => {
    expect(stayLanding.pricing).toBeUndefined()
    expect(stayLanding.media).toBeUndefined()
    expect(all).not.toMatch(/\/pricing|Тариф|₽|руб/iu)
  })

  it('forbidden words are absent everywhere', () => {
    const rules = [
      new RegExp(`${L}задат(ок|ка|ку|ком)${R}`, 'iu'),
      /невозвратн/iu,
      /депозит/iu,
      new RegExp(`${L}чек(а|и|ом|у|ов)?${R}`, 'iu'),
      new RegExp(`${L}бан(я|и|ь|ей|ю)${R}`, 'iu'),
      new RegExp(`${L}чан(ы|ов|ах)?${R}`, 'iu'),
    ]
    for (const r of rules) expect(all).not.toMatch(r)
  })

  it('owner section has no calls to action or promos', () => {
    for (const r of [/бесплатн/iu, /пробн/iu, /присоединяйтесь/iu, /начните/iu, /попробуйте/iu]) expect(business).not.toMatch(r)
  })

  it('header variant, facts and wording', () => {
    expect(stayLanding.hero.layout).toBe('panel')
    expect(stayLanding.hero.backdrop).toBe('mountains')
    expect(stayLanding.hero.facts).toHaveLength(3)
    expect(stayLanding.hero.intro).toContain('напрямую владельцу')
    expect(stayLanding.hero.eyebrow).toContain('Шерегеш')
  })

  it('FAQ 7 says only Sheregesh; brand is EZBOOK Дома, never «ezbook ·»', () => {
    expect(stayFaq[6].answer).toContain('Шерегеш')
    expect(all).not.toMatch(/ezbook ·/i)
    expect(JSON.stringify({ ...stayLanding, faq: null }).match(/EZBOOK Дома/g)).toBeNull()
    expect(JSON.stringify(stayFaq).match(/EZBOOK Дома/g)).toHaveLength(1)
  })

  it('owner buttons go to the registration / cabinet', () => {
    expect(stayLanding.business.actions[0]).toMatchObject({ kind: 'auth-route', guestTo: '/register?returnTo=%2Fcabinet%2Fnew', authedTo: '/cabinet/new' })
    expect(stayLanding.business.actions[1]).toMatchObject({ kind: 'route', to: '/cabinet', label: 'Войти в кабинет' })
  })
})
