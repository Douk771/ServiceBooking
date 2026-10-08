import { describe, it, expect } from 'vitest'
import { goodsFaq } from './goodsFaq'

// T38-09 (часть сервисов): конфиги — только данные, страницы главных — без собственной разметки секций.
const glob = (m: Record<string, string>) => Object.entries(m)
const configs = glob(
  import.meta.glob(['../../../src/pages/home/*.ts', './*.ts', '!**/*.test.ts'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>,
)
const pages = glob(
  import.meta.glob(['../../../src/pages/HomePage.tsx', '../pages/CatalogHomePage.tsx'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>,
)

describe('service landing guard (T38-09)', () => {
  it('finds configs and pages', () => {
    expect(configs.length).toBeGreaterThanOrEqual(4)
    expect(pages).toHaveLength(2)
  })
  it.each(configs)('%s: no className / JSX', (_f, src) => {
    expect(src).not.toContain('className')
    expect(src).not.toMatch(/<[A-Za-z]+[\s/>]/)
  })
  it.each(pages)('%s: no own section/h1/h2', (_f, src) => {
    expect(src).not.toMatch(/<section|<h1|<h2/)
  })
  it('goods FAQ has 6..8 items', () => {
    expect(goodsFaq.length).toBeGreaterThanOrEqual(6)
    expect(goodsFaq.length).toBeLessThanOrEqual(8)
  })
  it('goods FAQ does not link to or mention the pricing section (grid may be unpublished)', () => {
    expect(goodsFaq.some((i) => i.link?.to === '/pricing')).toBe(false)
    expect(JSON.stringify(goodsFaq)).not.toContain('разделе «Тарифы»')
  })
})
