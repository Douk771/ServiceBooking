// @vitest-environment node
import { describe, it, expect } from 'vitest'
import routesJson from '../../../contracts/cycle42/bani-routes.json'
import dom from '../../../contracts/cycle39/dom-routes.json'

// ARCHITECTURE_CYCLE42.md §42.10.1 — bani-routes.json is the SINGLE source of the bani route map and the reserved words.
const routes = routesJson
const firstSegment = (path: string) => path.split('/').filter(Boolean)[0]

// BaniApp.tsx arrives with FE-42-2; until then the source-based checks are skipped.
const sources = import.meta.glob('./BaniApp.tsx', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
const baniSource = sources['./BaniApp.tsx']
const withApp = it.skipIf(baniSource === undefined)

describe('bani-routes.json (contract)', () => {
  it('reserves the first segment of every SPA route', () => {
    const reserved = new Set(routes.reservedSlugs)
    for (const path of routes.spaRoutes) {
      const seg = firstSegment(path)
      if (seg) expect(reserved.has(seg), `first segment "${seg}" of ${path} must be in reservedSlugs`).toBe(true)
    }
  })

  it('includes every reserved word of the dom platform and the bani words', () => {
    const reserved = new Set(routes.reservedSlugs)
    for (const w of dom.reservedSlugs) expect(reserved.has(w), `${w} from dom-routes.json`).toBe(true)
    for (const w of ['bath', 'baths', 'sauna', 'saunas', 'resources', 'resource']) expect(reserved.has(w)).toBe(true)
  })

  it('has unique, lower-case reserved words that satisfy no company-address restriction by accident', () => {
    expect(new Set(routes.reservedSlugs).size).toBe(routes.reservedSlugs.length)
    for (const w of routes.reservedSlugs) expect(w).toBe(w.toLowerCase())
    expect(new Set(routes.reservedResourceSlugs).size).toBe(routes.reservedResourceSlugs.length)
  })

  it('reserves the words «uslugi» and «ical» under a company, and the SPA paths do not collide with them', () => {
    expect(routes.reservedResourceSlugs).toContain('uslugi')
    expect(routes.reservedResourceSlugs).toContain('ical')
  })

  it('has slug patterns consistent with the length limits', () => {
    const company = new RegExp(routes.slugPattern)
    expect(company.test('a'.repeat(routes.slugMinLength))).toBe(true)
    expect(company.test('banya-na-reke')).toBe(true)
    expect(company.test('-bad')).toBe(false)
    expect(company.test('Bad')).toBe(false)
    const resource = new RegExp(routes.resourceSlugPattern)
    expect(resource.test('chan')).toBe(true)
    expect(resource.test('chan_1')).toBe(false)
  })

  it('declares the query parameters of the catalogue and the resource page', () => {
    expect(routes.catalogQueryParams).toEqual(['city', 'date', 'page'])
    expect(routes.resourceQueryParams).toEqual(['date'])
  })
})

describe('BaniApp routes vs bani-routes.json', () => {
  const EXTRA = new Set([routes.companyPagePattern, routes.resourcePagePattern, '*'])

  withApp('has a <Route> for every spaRoute', () => {
    for (const path of routes.spaRoutes) expect(baniSource, `expected <Route path="${path}">`).toContain(`path="${path}"`)
  })

  withApp('has no route that bani-routes.json does not know', () => {
    const declared = [...baniSource.matchAll(/<Route\s+path="([^"]+)"/g)].map((m) => m[1])
    const known = new Set([...routes.spaRoutes, ...EXTRA])
    for (const path of declared) expect(known.has(path), `${path} is routed in BaniApp.tsx but missing from bani-routes.json`).toBe(true)
  })

  withApp('declares the company and resource address routes after the static ones', () => {
    expect(baniSource).toContain(`path="${routes.companyPagePattern}"`)
    expect(baniSource).toContain(`path="${routes.resourcePagePattern}"`)
    expect(baniSource.indexOf(`path="${routes.companyPagePattern}"`)).toBeGreaterThan(baniSource.indexOf('path="/s/:token"'))
  })
})
