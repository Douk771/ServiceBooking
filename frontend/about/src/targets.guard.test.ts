// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { PRODUCTS } from './content/products'

// CY44-08 (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.7): the anchor of "Для бизнеса" must survive renames in the four landing configs.
const raw = import.meta.glob(
  [
    '../../src/pages/home/zapisLanding.ts',
    '../../goods/src/landing/goodsLanding.ts',
    '../../dom/src/landing/stayLanding.ts',
    '../../bani/src/landing/baniLanding.ts',
  ],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>

describe('CY44-08 business anchors', () => {
  it('reads the four landing configs', () => expect(Object.keys(raw)).toHaveLength(4))

  it.each(Object.entries(raw))('%s: business section has titleId biz-title', (_file, src) => {
    const business = src.slice(src.indexOf('business:'))
    expect(business).toMatch(/titleId:\s*'biz-title'/)
  })

  it('every businessAction path in PRODUCTS points at that anchor', () => {
    for (const p of PRODUCTS) if (p.businessAction) expect(p.businessAction.path).toBe('/#biz-title')
  })
})
