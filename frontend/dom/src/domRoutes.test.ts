// @vitest-environment node
import { describe, it, expect } from 'vitest'
import routesJson from '../../../contracts/cycle39/dom-routes.json'
import legalJson from '../../../contracts/cycle11/legal-routes.json'
import domSource from './DomApp.tsx?raw'

// ARCHITECTURE_CYCLE39.md §39.14.1 — dom-routes.json is the SINGLE source of the dom route map and the reserved words.
interface DomRoutes {
  spaRoutes: string[]
  reservedSlugs: string[]
  companyPagePattern: string
  housePagePattern: string
  servicePagePattern: string
  serviceOrderPagePattern: string
  reservedHouseSlugs: string[]
}
const routes = routesJson as DomRoutes

const firstSegment = (path: string) => path.split('/').filter(Boolean)[0]
// Routes of DomApp that are not in `spaRoutes` on purpose: the two address patterns and the not-found catch-all.
const EXTRA = new Set([routes.companyPagePattern, routes.housePagePattern, routes.servicePagePattern, '*'])

describe('DomApp routes vs contracts/cycle39/dom-routes.json', () => {
  it('has a <Route> for every spaRoute', () => {
    for (const path of routes.spaRoutes) expect(domSource, `expected a <Route path="${path}"> in DomApp.tsx`).toContain(`path="${path}"`)
  })

  it('has no route that dom-routes.json does not know', () => {
    const declared = [...domSource.matchAll(/<Route\s+path="([^"]+)"/g)].map((m) => m[1])
    const known = new Set([...routes.spaRoutes, ...EXTRA])
    for (const path of declared) expect(known.has(path), `${path} is routed in DomApp.tsx but missing from dom-routes.json spaRoutes`).toBe(true)
  })

  it('reserves the first segment of every route, so a company address can never shadow it', () => {
    const reserved = new Set(routes.reservedSlugs)
    for (const path of routes.spaRoutes) {
      const seg = firstSegment(path)
      if (seg) expect(reserved.has(seg), `first segment "${seg}" of ${path} must be in reservedSlugs`).toBe(true)
    }
  })

  it('routes the service page /:slug/uslugi/:serviceSlug and the order page /s/:token, and reserves the word «uslugi» under a company', () => {
    expect(domSource).toContain(`path="${routes.servicePagePattern}"`)
    expect(routes.spaRoutes).toContain(routes.serviceOrderPagePattern)
    expect(routes.reservedHouseSlugs).toContain('uslugi')
    // the three-segment service route must rank by specificity, not by order, but is declared before the two-segment house route
    expect(domSource.indexOf(`path="${routes.servicePagePattern}"`)).toBeLessThan(domSource.indexOf(`path="${routes.housePagePattern}"`))
  })

  it('has the company and house address routes after the static ones', () => {
    expect(domSource.indexOf(`path="${routes.companyPagePattern}"`)).toBeGreaterThan(domSource.indexOf('path="/b/:token"'))
    expect(domSource).toContain(`path="${routes.housePagePattern}"`)
  })
})

describe('DomApp legal routes vs contracts/cycle11/legal-routes.json', () => {
  const legal = legalJson as { documents: Record<string, string>; aliases: Record<string, string> }
  const esc = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

  it('has a <Route> for every legal document path with the matching type', () => {
    for (const [type, path] of Object.entries(legal.documents)) {
      expect(domSource, `${type} at ${path}`).toMatch(new RegExp(`<Route\\s+path="${esc(path)}"\\s+element=\\{<LegalDocumentPage type="${type}"\\s*/>\\}`))
    }
  })

  it('has a redirect (or a real route for a self-mapped alias) for every alias', () => {
    for (const [alias, target] of Object.entries(legal.aliases)) {
      if (alias === target) expect(domSource).toMatch(new RegExp(`<Route\\s+path="${esc(alias)}"`))
      else expect(domSource).toMatch(new RegExp(`<Route\\s+path="${esc(alias)}"[\\s\\S]*?to="${esc(target)}"`))
    }
  })

  it('lists every legal path and alias in the consent-gate bypass list', () => {
    const m = domSource.match(/CONSENT_GATE_BYPASS_PATHS = \[([\s\S]*?)\]/)
    expect(m).not.toBeNull()
    for (const path of [...Object.values(legal.documents), ...Object.keys(legal.aliases)]) expect(m![1]).toContain(`'${path}'`)
  })
})
