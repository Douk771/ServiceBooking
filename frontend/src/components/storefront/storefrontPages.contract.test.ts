// @vitest-environment node
import { describe, expect, it } from 'vitest'
import pagesJson from '../../../../contracts/cycle48/storefront-pages.json'
// `?raw`: the legal opinion is read as a string, no Node `fs` (same approach as src/legalRoutes.test.ts).
import legalReview from '../../../../LEGAL_REVIEW_CYCLE48.md?raw'

// CY48-05 (API_CONTRACT_CYCLE48_STOREFRONT.md §48.22.4): every text string of storefront-pages.json is a verbatim quote of the legal opinion.
function normalize(s: string): string {
  return s
    .replace(/^\s*>\s?/gm, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[\*\]\s/g, '')
    .replace(/`/g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

const SKIP_KEYS = new Set(['sites', 'type', 'id', 'path', 'contract', 'version', 'placeholders', 'markup', 'source'])

function collect(node: unknown, out: string[], key = '', parentKey = ''): void {
  if (key.startsWith('_') || key.startsWith('legacy') || key.startsWith('derived') || SKIP_KEYS.has(key)) return
  if (typeof node === 'string') {
    out.push(node)
  } else if (Array.isArray(node)) {
    // tariffs.*.highlights: items 1-3 are unchanged old text, only the last one is new.
    const items = parentKey === 'tariffs' && key === 'highlights' ? node.slice(-1) : node
    for (const item of items) collect(item, out, '', key)
  } else if (node !== null && typeof node === 'object') {
    for (const [k, v] of Object.entries(node)) collect(v, out, k, k === 'highlights' ? parentKeyOf(key, parentKey) : key)
  }
}
// `highlights` sits under tariffs.<name>: remember that the grandparent is `tariffs`.
function parentKeyOf(key: string, parentKey: string): string {
  return ['zapisNetwork', 'ordersChain', 'zapisTrial'].includes(key) ? 'tariffs' : parentKey
}

describe('storefront-pages.json vs LEGAL_REVIEW_CYCLE48.md', () => {
  const normLegal = normalize(legalReview)
  const strings: string[] = []
  collect(pagesJson, strings)

  it('collects a meaningful number of strings', () => {
    expect(strings.length).toBeGreaterThan(40)
  })

  it('every string is a verbatim quote of the legal opinion', () => {
    const missing = strings.filter((s) => !normLegal.includes(normalize(s)))
    expect(missing, `not found in LEGAL_REVIEW_CYCLE48.md:\n${missing.join('\n')}`).toEqual([])
  })

  // «Поправка 31.10.2026 (2)»: «Заказы» показывают тот же короткий текст, что и «Запись».
  it('«Заказы» notice is the «Записи» notice', () => {
    const { zapis, derivedOrders } = pagesJson.pricing.notice
    expect(derivedOrders).toBe(zapis)
  })
})
