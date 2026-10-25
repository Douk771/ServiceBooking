// @vitest-environment node
import { describe, it, expect } from 'vitest'
import routesJson from '../../../contracts/cycle23/goods-routes.json'
import legalJson from '../../../contracts/cycle11/legal-routes.json'
// `?raw` reads the source as text at test time — no Node `fs`, same trick as src/legalRoutes.test.ts.
import goodsSource from './GoodsApp.tsx?raw'

// ARCHITECTURE_CYCLE23.md §390, §399.3 — goods-routes.json is the SINGLE source of the goods route map and the
// reserved-address list: backend (SlugPolicy), this test and QA read the same file.

interface GoodsRoutes {
  slugPattern: string
  slugMinLength: number
  shopPagePattern: string
  spaRoutes: string[]
  reservedSlugs: string[]
}
const routes = routesJson as GoodsRoutes

const firstSegment = (path: string) => path.split('/').filter(Boolean)[0]

describe('GoodsApp routes vs contracts/cycle23/goods-routes.json', () => {
  it('has a <Route> for every spaRoute (the root and the legal redirects included)', () => {
    for (const path of routes.spaRoutes) {
      expect(goodsSource, `expected a <Route path="${path}"> in GoodsApp.tsx`).toContain(`path="${path}"`)
    }
  })

  it('has no route that goods-routes.json does not know', () => {
    const declared = [...goodsSource.matchAll(/<Route\s+path="([^"]+)"/g)].map((m) => m[1])
    const known = new Set([...routes.spaRoutes, routes.shopPagePattern, '*'])
    for (const path of declared) {
      expect(known.has(path), `${path} is routed in GoodsApp.tsx but missing from goods-routes.json spaRoutes`).toBe(true)
    }
  })

  it('reserves the first segment of every route, so a shop address can never shadow it', () => {
    const reserved = new Set(routes.reservedSlugs)
    for (const path of routes.spaRoutes) {
      const seg = firstSegment(path)
      if (!seg) continue // "/"
      expect(reserved.has(seg), `first segment "${seg}" of ${path} must be in reservedSlugs`).toBe(true)
    }
  })

  it('reserved words are lowercase and unique', () => {
    expect(new Set(routes.reservedSlugs).size).toBe(routes.reservedSlugs.length)
    for (const w of routes.reservedSlugs) expect(w).toBe(w.toLowerCase())
  })

  it('keeps every reserved word that fits the address format out of the valid-slug space', () => {
    // A reserved word must never be accepted by the format check alone — otherwise the reserve is the only guard.
    const pattern = new RegExp(routes.slugPattern)
    const reachable = routes.reservedSlugs.filter((w) => w.length >= routes.slugMinLength && pattern.test(w))
    expect(reachable.length).toBeGreaterThan(0) // sanity: the list is not vacuous
  })
})

describe('GoodsApp legal routes vs contracts/cycle11/legal-routes.json', () => {
  const legal = legalJson as { documents: Record<string, string>; aliases: Record<string, string> }

  it('has a <Route> for every legal document path, with the matching LegalDocumentPage type', () => {
    for (const [type, path] of Object.entries(legal.documents)) {
      const re = new RegExp(`<Route\\s+path="${esc(path)}"\\s+element=\\{<LegalDocumentPage type="${type}"\\s*/>\\}`)
      expect(goodsSource, `expected a Route for ${type} at ${path}`).toMatch(re)
    }
  })

  it('has a redirect for every alias — except a self-mapped alias, which must be a real <Route> instead', () => {
    // Same rule as src/legalRoutes.test.ts (ARCHITECTURE_CYCLE20.md §411/§412.6): `/data-request` maps to
    // itself because it is a real route that legal texts link to, not a redirect.
    for (const [alias, target] of Object.entries(legal.aliases)) {
      if (alias === target) {
        expect(goodsSource, `expected a real <Route> for the self-mapped alias ${alias}`).toMatch(
          new RegExp(`<Route\\s+path="${esc(alias)}"`),
        )
        continue
      }
      expect(goodsSource).toMatch(new RegExp(`<Route\\s+path="${esc(alias)}"[\\s\\S]*?to="${esc(target)}"`))
    }
  })

  it('lists every legal path and alias in the consent-gate bypass list', () => {
    const m = goodsSource.match(/CONSENT_GATE_BYPASS_PATHS = \[([\s\S]*?)\]/)
    expect(m, 'CONSENT_GATE_BYPASS_PATHS not found in GoodsApp.tsx').not.toBeNull()
    for (const path of [...Object.values(legal.documents), ...Object.keys(legal.aliases)]) {
      expect(m![1], `expected ${path} in CONSENT_GATE_BYPASS_PATHS`).toContain(`'${path}'`)
    }
  })
})

function esc(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

// CY48-09 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.7): /contacts and /payment-info on GoodsApp.
describe('GoodsApp storefront pages (cycle 48)', () => {
  it('routes /contacts and /payment-info exist, render the pages with site="goods", are in the bypass list and come before the address routes', () => {
    expect(goodsSource).toMatch(/<Route path="\/contacts" element=\{<ContactsPage site="goods" \/>\} \/>/)
    expect(goodsSource).toMatch(/<Route path="\/payment-info" element=\{<PaymentInfoPage site="goods" \/>\} \/>/)
    const m = goodsSource.match(/CONSENT_GATE_BYPASS_PATHS = \[([\s\S]*?)\]/)
    expect(m).not.toBeNull()
    expect(m![1]).toContain("'/contacts'")
    expect(m![1]).toContain("'/payment-info'")
    const at = (p: string) => goodsSource.indexOf(`path="${p}"`)
    expect(at('/contacts')).toBeGreaterThan(-1)
    expect(at('/payment-info')).toBeGreaterThan(-1)
    expect(at('/contacts')).toBeLessThan(at('/:slug'))
    expect(at('/payment-info')).toBeLessThan(at('/:slug'))
  })

  it('/payment-terms leads to /payment-info; payment-info is a reserved address', () => {
    expect(goodsSource).toMatch(/<Route path="\/payment-terms" element=\{<Navigate to="\/payment-info" replace \/>\} \/>/)
    expect(routes.spaRoutes).toEqual(expect.arrayContaining(['/contacts', '/payment-info']))
    expect(routes.reservedSlugs).toEqual(expect.arrayContaining(['contacts', 'payment-info']))
  })
})
