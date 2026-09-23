import { describe, it, expect } from 'vitest'
import routesJson from '../../contracts/cycle11/legal-routes.json'
// `?raw` (vite/client.d.ts) reads the file as a plain string at build/test time — no Node `fs`
// needed, so this stays typecheckable under the project's `src`-only tsconfig (no @types/node).
import appSource from './App.tsx?raw'

// ARCHITECTURE_CYCLE11.md §104.3, §109.1 (T6), §118 — legal-routes.json is the SINGLE source of the
// legal route map; backend, the `legal links` CLI and this frontend test all read the same file, so
// the three sides can never disagree with each other without disagreeing with themselves.

interface LegalRoutesFile {
  documents: Record<string, string>
  aliases: Record<string, string>
  anchors: Record<string, string[]>
  allowedTargets: string[]
}

const routes = routesJson as LegalRoutesFile

describe('legal routes vs App.tsx (contracts/cycle11/legal-routes.json)', () => {
  it('has a <Route> for every document path, rendering the matching LegalDocumentPage type', () => {
    for (const [documentType, path] of Object.entries(routes.documents)) {
      const routeRe = new RegExp(
        `<Route\\s+path="${escapeRegExp(path)}"\\s+element=\\{<LegalDocumentPage type="${documentType}"\\s*/>\\}`,
      )
      expect(appSource, `expected a Route for ${documentType} at ${path}`).toMatch(routeRe)
    }
  })

  it('has a redirect for every alias, pointing at its target', () => {
    for (const [aliasPath, target] of Object.entries(routes.aliases)) {
      const redirectRe = new RegExp(`<Route\\s+path="${escapeRegExp(aliasPath)}"[\\s\\S]*?to="${escapeRegExp(target)}"`)
      expect(appSource, `expected ${aliasPath} to redirect to ${target}`).toMatch(redirectRe)
    }
  })

  it('lists every document path and alias in the consent-gate bypass list', () => {
    // CONSENT_GATE_BYPASS_PATHS must stay reachable during a pending 451 (App.tsx comment,
    // API_CONTRACT_CYCLE5.md §41, §48.4) — a document path missing from it would lock a user out of
    // the very page that lets them accept and clear the gate.
    const allPaths = [...Object.values(routes.documents), ...Object.keys(routes.aliases)]
    const bypassMatch = appSource.match(/CONSENT_GATE_BYPASS_PATHS = \[([\s\S]*?)\]/)
    expect(bypassMatch, 'CONSENT_GATE_BYPASS_PATHS not found in App.tsx').not.toBeNull()
    const bypassList = bypassMatch![1]
    for (const path of allPaths) {
      expect(bypassList, `expected ${path} in CONSENT_GATE_BYPASS_PATHS`).toContain(`'${path}'`)
    }
  })

  it('does not define allowedTargets that App.tsx has no route for', () => {
    const knownPaths = new Set([...Object.values(routes.documents), ...Object.keys(routes.aliases)])
    for (const target of routes.allowedTargets) {
      expect(knownPaths.has(target), `${target} listed in allowedTargets but not in documents/aliases`).toBe(true)
    }
  })
})

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}
