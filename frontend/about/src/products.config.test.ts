import { describe, it, expect } from 'vitest'
import { PRODUCTS } from './content/products'
import { PAGE_COPY } from './content/page'
import { DEMO_SITE_URLS } from '@/utils/publicSites'

// CY44-01 (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.2, инварианты).
const FORBIDDEN = [/лучш/i, /№\s?1/, /самый/i, /самая/i, /бесплатн/i, /без комиссии/i, /₽/, /от \d+\s?руб/i]
const FORBIDDEN_FOR_TESTING = [/подключ/i, /попробуй/i, /зарегистр/i]

function texts(p: (typeof PRODUCTS)[number]): string[] {
  return [p.name, p.forWhom, ...p.features, p.forClients, p.clientAction.label, p.businessAction?.label ?? '', p.demo?.label ?? '', p.testingNote ?? '']
}

describe('CY44-01 PRODUCTS config', () => {
  it('keys are unique and all four products are present once', () => {
    expect(PRODUCTS.map((p) => p.key).sort()).toEqual(['bani', 'dom', 'goods', 'zapis'])
  })

  it('customer defaults of 20.10.2026: statuses and order', () => {
    expect(PRODUCTS.map((p) => [p.key, p.status])).toEqual([
      ['zapis', 'live'],
      ['goods', 'live'],
      ['dom', 'live'],
      ['bani', 'live'],
    ])
  })

  it('demo stands: Запись -> demo.visit, Заказы -> demo.zakaz, no demo for Дома and Бани', () => {
    expect(DEMO_SITE_URLS).toEqual({ zapis: 'https://demo.visit.ezbook.ru', goods: 'https://demo.zakaz.ezbook.ru' })
    expect(PRODUCTS.filter((p) => p.demo).map((p) => p.key)).toEqual(['zapis', 'goods'])
  })

  it.each(Object.entries(DEMO_SITE_URLS))('%s: demo origin is https host only', (_key, origin) => {
    const url = new URL(origin!)
    expect(url.protocol).toBe('https:')
    expect(url.hostname).toMatch(/^demo\.[a-z0-9.-]+\.ezbook\.ru$/)
    expect(url.origin).toBe(origin)
  })

  it('every product with a demo label has a demo address and a non-empty label', () => {
    for (const p of PRODUCTS) if (p.demo) {
      expect(DEMO_SITE_URLS[p.key], p.key).toBeDefined()
      expect(p.demo.label.trim()).not.toBe('')
    }
  })

  it.each(PRODUCTS.map((p) => [p.key, p] as const))('%s: non-empty fields and 3..5 features', (_key, p) => {
    expect(p.features.length).toBeGreaterThanOrEqual(3)
    expect(p.features.length).toBeLessThanOrEqual(5)
    for (const t of [p.name, p.forWhom, p.forClients, p.clientAction.label, ...p.features]) expect(t.trim()).not.toBe('')
    expect(p.name.startsWith('EZBOOK ')).toBe(true)
  })

  it.each(PRODUCTS.map((p) => [p.key, p] as const))('%s: relative paths without scheme, host or "//"', (_key, p) => {
    for (const a of [p.clientAction, p.businessAction].filter((x) => x !== undefined)) {
      expect(a.path.startsWith('/')).toBe(true)
      expect(a.path).not.toMatch(/\/\/|:/)
    }
  })

  it('testing products have a note and no business action; live have a business action', () => {
    for (const p of PRODUCTS) {
      if (p.status === 'testing') {
        expect(p.testingNote.trim()).not.toBe('')
        expect(p.businessAction).toBeUndefined()
      }
      if (p.status === 'live') expect(p.businessAction.path).toBe('/#biz-title')
    }
  })

  it('no advertising or price words; no call to join in testing cards', () => {
    for (const p of PRODUCTS) {
      for (const t of texts(p)) for (const re of FORBIDDEN) expect(t, `${p.key}: ${t}`).not.toMatch(re)
      if (p.status === 'testing') for (const t of texts(p)) for (const re of FORBIDDEN_FOR_TESTING) expect(t, `${p.key}: ${t}`).not.toMatch(re)
    }
    const page = [PAGE_COPY.meta.title, PAGE_COPY.meta.description, PAGE_COPY.hero.title, PAGE_COPY.hero.subtitle, ...PAGE_COPY.account.paragraphs]
    for (const t of page) for (const re of FORBIDDEN) expect(t).not.toMatch(re)
  })
})
