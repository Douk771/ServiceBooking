// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  SHARED_EZBOOK_PAGES,
  SCANNED_EZBOOK_DIRS,
  MARKUP_FREE_EZBOOK_DIRS,
  SHARED_EZBOOK_FILES,
  appTailwindContent,
} from '../../shared-sources.js'
import domTailwindConfig from '../../tailwind.dom.config.js'

// ARCHITECTURE_CYCLE37.md §37.14.4 (logic of goods/src/sharedSources.guard.test.ts, ARCHITECTURE_CYCLE31.md §31.7.3) — the only
// automatic protection against "dom imports an ezbook file that the dom Tailwind build does not scan" (its classes silently vanish from dom CSS). Vite's
// `import.meta.glob` with `?raw` reads sources as text, so there is no Node `fs` here (same idea as goodsRoutes.test.ts).
const raw = import.meta.glob(['../../src/**/*.{ts,tsx}', './**/*.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

// repo-relative path (from frontend/) -> source
const files = new Map<string, string>()
for (const [key, src] of Object.entries(raw)) {
  files.set(key.startsWith('../../src/') ? key.slice('../../'.length) : `dom/src/${key.slice(2)}`, src)
}

const isTest = (p: string) => /\.test\.tsx?$/.test(p)
const IMPORT_RE = /(?:import|export)\s+(?:type\s+)?(?:[^'"()]*?\sfrom\s+)?['"]([^'"]+)['"]|import\(\s*['"]([^'"]+)['"]\s*\)/g

function resolveSpecifier(from: string, spec: string): string | null {
  let base: string
  if (spec.startsWith('@/')) base = `src/${spec.slice(2)}`
  else if (spec.startsWith('@dom/')) base = `dom/src/${spec.slice('@dom/'.length)}`
  else if (spec.startsWith('.')) {
    const parts = from.split('/').slice(0, -1)
    for (const seg of spec.split('/')) {
      if (seg === '.' || seg === '') continue
      if (seg === '..') parts.pop()
      else parts.push(seg)
    }
    base = parts.join('/')
  } else return null // npm package
  for (const ext of ['', '.ts', '.tsx', '/index.ts', '/index.tsx']) {
    if (files.has(base + ext)) return base + ext
  }
  return null // css/svg/json or a non-existent path (tsc reports those)
}

function reachedFromDom(): Set<string> {
  const seen = new Set<string>()
  const stack = [...files.keys()].filter((p) => p.startsWith('dom/src/') && !isTest(p))
  while (stack.length) {
    const file = stack.pop()!
    if (seen.has(file)) continue
    seen.add(file)
    for (const m of files.get(file)!.matchAll(IMPORT_RE)) {
      const target = resolveSpecifier(file, (m[1] ?? m[2])!)
      if (target && !seen.has(target) && !isTest(target)) stack.push(target)
    }
  }
  return seen
}

function isCovered(p: string): boolean {
  if (SCANNED_EZBOOK_DIRS.some((d) => p.startsWith(`src/${d}/`))) return true
  if (SHARED_EZBOOK_PAGES.some((pg) => p === `src/pages/${pg}.tsx`)) return true
  if (SHARED_EZBOOK_FILES.includes(p.slice('src/'.length))) return true
  return MARKUP_FREE_EZBOOK_DIRS.some((d) => p.startsWith(`src/${d}/`)) && p.endsWith('.ts')
}

describe('dom shared ezbook sources (T-31-01)', () => {
  it('every ezbook file reachable from dom is scanned by the dom Tailwind build', () => {
    const uncovered = [...reachedFromDom()].filter((p) => p.startsWith('src/') && !isCovered(p))
    expect(
      uncovered,
      uncovered
        .map(
          (p) =>
            `${p} импортируется dom, но не сканируется tailwind.dom.config.js — перенесите его в src/components/ или впишите в frontend/shared-sources.js`,
        )
        .join('\n'),
    ).toEqual([])
  })

  it('sanity: the walk actually reaches shared components', () => {
    const reached = reachedFromDom()
    expect(reached.has('src/components/ui/Button.tsx')).toBe(true)
  })

  it('every listed shared page exists as a file', () => {
    for (const page of SHARED_EZBOOK_PAGES) {
      expect(files.has(`src/pages/${page}.tsx`), `src/pages/${page}.tsx`).toBe(true)
    }
  })

  it('markup-free dirs contain no .tsx', () => {
    const offenders = [...files.keys()].filter(
      (p) => p.endsWith('.tsx') && MARKUP_FREE_EZBOOK_DIRS.some((d) => p.startsWith(`src/${d}/`)),
    )
    expect(offenders).toEqual([])
  })

  it('tailwind.dom.config.js content is generated from the list, not hand-edited', () => {
    expect(domTailwindConfig.content).toEqual(appTailwindContent('dom'))
  })
})
