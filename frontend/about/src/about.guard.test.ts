// @vitest-environment node
import { describe, it, expect } from 'vitest'

// CY44-03 (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.14): static guard over the sources of about/src (no tests).
const raw = import.meta.glob(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>
// Comments may legitimately mention the forbidden words.
const stripComments = (src: string) => src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '').replace(/\s\/\/ .*$/gm, '')
const sources = Object.entries(raw).map(([f, src]) => [f, stripComments(src)] as const)

const RULES: Array<[string, RegExp]> = [
  ['hard-coded ezbook address (use src/utils/publicSites.ts)', /https?:\/\/[a-z0-9.-]*ezbook\.ru/i],
  ['/api path', /['"`]\/api\b/],
  ['dangerouslySetInnerHTML', /dangerouslySetInnerHTML/],
  ['style attribute (blocked by the CSP)', /\bstyle\s*[=:]\s*[{"']/],
  ['React state/effect hook', /\buse(State|Effect|Reducer|LayoutEffect|Ref|Context|Memo|Callback)\b/],
  ['react-router', /react-router/],
  ['storage / cookie / service worker', /localStorage|sessionStorage|document\.cookie|serviceWorker/],
  ['wrong brand spelling', /ezbook\s·|Ezbook/],
  ['window/document access', /\b(window|document)\./],
]

describe('CY44-03 about sources', () => {
  it('sees the sources', () => expect(sources.length).toBeGreaterThan(8))
  it.each(RULES)('no %s', (_name, re) => {
    const hits = sources.filter(([, src]) => re.test(src)).map(([f]) => f)
    expect(hits).toEqual([])
  })
})
