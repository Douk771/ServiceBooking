// @vitest-environment node
import { describe, it, expect } from 'vitest'
import routesJson from '../../../contracts/cycle42/bani-routes.json'
import legalJson from '../../../contracts/cycle11/legal-routes.json'
import baniSource from './BaniApp.tsx?raw'

// ARCHITECTURE_CYCLE42.md §42.10.1 — bani-routes.json is the SINGLE source of the bani route map and the reserved words.
interface BaniRoutes {
  spaRoutes: string[]
  reservedSlugs: string[]
  reservedResourceSlugs: string[]
  companyPagePattern: string
  resourcePagePattern: string
  orderPagePattern: string
}
const routes = routesJson as BaniRoutes

const firstSegment = (path: string) => path.split('/').filter(Boolean)[0]
// Routes of BaniApp that are not in `spaRoutes` on purpose: the two address patterns and the not-found catch-all.
const EXTRA = new Set([routes.companyPagePattern, routes.resourcePagePattern, '*'])

describe('BaniApp routes vs contracts/cycle42/bani-routes.json', () => {
  it('has a <Route> for every spaRoute', () => {
    for (const path of routes.spaRoutes) expect(baniSource, `expected a <Route path="${path}"> in BaniApp.tsx`).toContain(`path="${path}"`)
  })

  it('has no route that bani-routes.json does not know', () => {
    const declared = [...baniSource.matchAll(/<Route\s+path="([^"]+)"/g)].map((m) => m[1])
    const known = new Set([...routes.spaRoutes, ...EXTRA])
    for (const path of declared) expect(known.has(path), `${path} is routed in BaniApp.tsx but missing from bani-routes.json spaRoutes`).toBe(true)
  })

  it('reserves the first segment of every route, so a company address can never shadow it', () => {
    const reserved = new Set(routes.reservedSlugs)
    for (const path of routes.spaRoutes) {
      const seg = firstSegment(path)
      if (seg) expect(reserved.has(seg), `first segment "${seg}" of ${path} must be in reservedSlugs`).toBe(true)
    }
  })

  it('declares the company and resource address routes after the static ones, the catch-all last', () => {
    const at = (p: string) => baniSource.indexOf(`path="${p}"`)
    expect(at(routes.companyPagePattern)).toBeGreaterThan(at('/payment-terms'))
    expect(at(routes.resourcePagePattern)).toBeGreaterThan(at(routes.companyPagePattern))
    expect(at('*')).toBeGreaterThan(at(routes.resourcePagePattern))
  })
})

describe('BaniApp legal routes vs contracts/cycle11/legal-routes.json', () => {
  const legal = legalJson as { documents: Record<string, string>; aliases: Record<string, string> }
  const esc = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

  it('has a <Route> for every legal document path with the matching type', () => {
    for (const [type, path] of Object.entries(legal.documents)) {
      expect(baniSource, `${type} at ${path}`).toMatch(new RegExp(`<Route\\s+path="${esc(path)}"\\s+element=\\{<LegalDocumentPage type="${type}"\\s*/>\\}`))
    }
  })

  it('has a redirect (or a real route for a self-mapped alias) for every alias', () => {
    for (const [alias, target] of Object.entries(legal.aliases)) {
      if (alias === target) expect(baniSource).toMatch(new RegExp(`<Route\\s+path="${esc(alias)}"`))
      else expect(baniSource).toMatch(new RegExp(`<Route\\s+path="${esc(alias)}"[\\s\\S]*?to="${esc(target)}"`))
    }
  })

  it('lists every legal path and alias in the consent-gate bypass list', () => {
    const m = baniSource.match(/CONSENT_GATE_BYPASS_PATHS = \[([\s\S]*?)\]/)
    expect(m).not.toBeNull()
    for (const path of [...Object.values(legal.documents), ...Object.keys(legal.aliases)]) expect(m![1]).toContain(`'${path}'`)
  })
})
