// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  GOODS_SHARED_EZBOOK_PAGES,
  GOODS_SCANNED_EZBOOK_DIRS,
  GOODS_MARKUP_FREE_EZBOOK_DIRS,
  GOODS_SHARED_EZBOOK_FILES,
  goodsTailwindContent,
} from '../../goods-shared-sources.js'
import goodsTailwindConfig from '../../tailwind.goods.config.js'

// ARCHITECTURE_CYCLE31.md §31.7.3 (T-31-01, C31-1) — the only automatic protection against "goods imports an ezbook
// file that the goods Tailwind build does not scan" (its classes silently vanish from goods CSS). Vite's
// `import.meta.glob` with `?raw` reads sources as text, so there is no Node `fs` here (same idea as goodsRoutes.test.ts).
const raw = import.meta.glob(['../../src/**/*.{ts,tsx}', './**/*.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

// repo-relative path (from frontend/) -> source
const files = new Map<string, string>()
for (const [key, src] of Object.entries(raw)) {
  files.set(key.startsWith('../../src/') ? key.slice('../../'.length) : `goods/src/${key.slice(2)}`, src)
}

const isTest = (p: string) => /\.test\.tsx?$/.test(p)
const IMPORT_RE = /(?:import|export)\s+(?:type\s+)?(?:[^'"()]*?\sfrom\s+)?['"]([^'"]+)['"]|import\(\s*['"]([^'"]+)['"]\s*\)/g

function resolveSpecifier(from: string, spec: string): string | null {
  let base: string
  if (spec.startsWith('@/')) base = `src/${spec.slice(2)}`
  else if (spec.startsWith('@goods/')) base = `goods/src/${spec.slice('@goods/'.length)}`
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

function reachedFromGoods(): Set<string> {
  const seen = new Set<string>()
  const stack = [...files.keys()].filter((p) => p.startsWith('goods/src/') && !isTest(p))
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
  if (GOODS_SCANNED_EZBOOK_DIRS.some((d) => p.startsWith(`src/${d}/`))) return true
  if (GOODS_SHARED_EZBOOK_PAGES.some((pg) => p === `src/pages/${pg}.tsx`)) return true
  if (GOODS_SHARED_EZBOOK_FILES.includes(p.slice('src/'.length))) return true
  return GOODS_MARKUP_FREE_EZBOOK_DIRS.some((d) => p.startsWith(`src/${d}/`)) && p.endsWith('.ts')
}

describe('goods shared ezbook sources (T-31-01)', () => {
  it('every ezbook file reachable from goods is scanned by the goods Tailwind build', () => {
    const uncovered = [...reachedFromGoods()].filter((p) => p.startsWith('src/') && !isCovered(p))
    expect(
      uncovered,
      uncovered
        .map(
          (p) =>
            `${p} импортируется goods, но не сканируется tailwind.goods.config.js — перенесите его в src/components/ или впишите в frontend/goods-shared-sources.js`,
        )
        .join('\n'),
    ).toEqual([])
  })

  it('sanity: the walk actually reaches shared components', () => {
    const reached = reachedFromGoods()
    expect(reached.has('src/components/company/CompanyPhotosSection.tsx')).toBe(true)
  })

  it('every listed shared page exists as a file', () => {
    for (const page of GOODS_SHARED_EZBOOK_PAGES) {
      expect(files.has(`src/pages/${page}.tsx`), `src/pages/${page}.tsx`).toBe(true)
    }
  })

  it('markup-free dirs contain no .tsx', () => {
    const offenders = [...files.keys()].filter(
      (p) => p.endsWith('.tsx') && GOODS_MARKUP_FREE_EZBOOK_DIRS.some((d) => p.startsWith(`src/${d}/`)),
    )
    expect(offenders).toEqual([])
  })

  it('tailwind.goods.config.js content is generated from the list, not hand-edited', () => {
    expect(goodsTailwindConfig.content).toEqual(goodsTailwindContent())
  })
})
