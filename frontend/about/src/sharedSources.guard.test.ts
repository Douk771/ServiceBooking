// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { ABOUT_SHARED_SOURCES, aboutTailwindContent, appTailwindContent } from '../../shared-sources.js'
import aboutTailwindConfig from '../../tailwind.about.config.js'

// ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.5: imports of about/src from the shared code are a subset of ABOUT_SHARED_SOURCES.
const raw = import.meta.glob(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>
const IMPORT_RE = /(?:import|export)\s+(?:type\s+)?(?:[^'"()]*?\sfrom\s+)?['"](@\/[^'"]+)['"]/g

describe('about shared sources', () => {
  it('every "@/..." import resolves into the whitelist', () => {
    const used = new Set<string>()
    for (const src of Object.values(raw)) for (const m of src.matchAll(IMPORT_RE)) used.add(m[1].slice(2))
    expect(used.size).toBeGreaterThan(0)
    const allowed = ABOUT_SHARED_SOURCES.map((f) => f.replace(/^src\//, ''))
    for (const spec of used) {
      expect(allowed.some((f) => f === spec || f.replace(/\.tsx?$/, '') === spec), `${spec} is not in ABOUT_SHARED_SOURCES`).toBe(true)
    }
  })

  it('the Tailwind config scans the own sources and the whitelist, not the whole src/', () => {
    expect(aboutTailwindConfig.content).toEqual(aboutTailwindContent())
    expect(aboutTailwindConfig.content.join('\n')).not.toContain('src/components/**')
  })

  it('about is not a secondary app', () => {
    expect(() => appTailwindContent('about' as never)).toThrow()
  })
})
