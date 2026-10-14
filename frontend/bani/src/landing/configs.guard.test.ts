import { describe, it, expect } from 'vitest'
import { baniFaq } from './baniFaq'
import { baniLanding } from './baniLanding'

// FE-42-3 / LEGAL_REVIEW_CYCLE42.md §7.2, §10: конфиг главной «Бань» — только данные; на главной и в FAQ нет запрещённых обещаний.
const raw = (m: Record<string, string>) => Object.entries(m)
const configs = raw(import.meta.glob(['./baniLanding.ts', './baniFaq.ts'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>)
const [[, pageSrc]] = raw(import.meta.glob('../pages/CatalogPage.tsx', { query: '?raw', import: 'default', eager: true }) as Record<string, string>)

const all = JSON.stringify({ baniLanding, baniFaq })
const business = JSON.stringify(baniLanding.business)
const L = '(?<!\\p{L})'
const R = '(?!\\p{L})'

describe('bani landing guard', () => {
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

  it('FAQ has 6..8 items, none with a link', () => {
    expect(baniFaq.length).toBeGreaterThanOrEqual(6)
    expect(baniFaq.length).toBeLessThanOrEqual(8)
    expect(baniFaq.some((i) => i.link)).toBe(false)
  })

  it('has no pricing: no tariffs, no media, no prices', () => {
    expect(baniLanding.pricing).toBeUndefined()
    expect(baniLanding.media).toBeUndefined()
    expect(all).not.toMatch(/\/pricing|тариф|₽|руб/iu)
  })

  it('forbidden words are absent everywhere (§10)', () => {
    const rules = [
      /задат/iu,
      /невозвратн/iu,
      /депозит/iu,
      /бесплатн/iu,
      /гарантир/iu,
      /лучш/iu,
      new RegExp(`${L}самы[ейх]${R}`, 'iu'),
      /№\s?1/u,
      /провер/iu,
      /мгновенн/iu,
      /онлайн-оплат/iu,
      /оплата (картой|на сайте)/iu,
      /аренд/iu,
      /прокат/iu,
      /лечебн/iu,
      /оздоров/iu,
      /детокс/iu,
      /иммунитет/iu,
      /штраф|неустойк/iu,
      /бизнес-день/iu,
      new RegExp(`${L}чек(а|и|ом|у|ов)?${R}`, 'iu'),
      new RegExp(`${L}дом(а|ов|у|е|ом|ам|ах)?${R}`, 'iu'),
      /проживани|заселени|заезд/iu,
      /туристическ/iu,
      /push/iu,
    ]
    for (const r of rules) expect(all).not.toMatch(r)
  })

  it('owner section has no calls to action or promos', () => {
    for (const r of [/бесплатн/iu, /пробн/iu, /присоединяйтесь/iu, /начните/iu, /попробуйте/iu]) expect(business).not.toMatch(r)
  })

  it('header: panel without backdrop, three facts; the brand is spelled once, in the FAQ', () => {
    expect(baniLanding.hero.layout).toBe('panel')
    expect(baniLanding.hero.backdrop).toBe('none')
    expect(baniLanding.hero.facts).toHaveLength(3)
    expect(JSON.stringify({ ...baniLanding, faq: null }).match(/EZBOOK Бани/g)).toBeNull()
    expect(JSON.stringify(baniFaq).match(/EZBOOK Бани/g)).toHaveLength(1)
    expect(all).not.toMatch(/ezbook ·/i)
  })

  it('owner buttons go to the registration / cabinet', () => {
    expect(baniLanding.business.actions[0]).toMatchObject({ kind: 'auth-route', guestTo: '/register?returnTo=%2Fcabinet%2Fnew', authedTo: '/cabinet/new' })
    expect(baniLanding.business.actions[1]).toMatchObject({ kind: 'route', to: '/cabinet', label: 'Войти в кабинет' })
  })
})
