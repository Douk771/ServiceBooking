// API_CONTRACT_CYCLE48_STOREFRONT.md §48.22 — types of contracts/cycle48/storefront-pages.json and the parser of its mini-markup
// (`**bold**`, `*italic*`, `[text](href)`, `{{KEY}}`). Pure module. The parser never interprets HTML and never silently prints raw
// text: an unclosed construct or a foreign href scheme throws StorefrontMarkupError, so a broken string fails a test.
import pagesJson from '../../../contracts/cycle48/storefront-pages.json'
import type { StorefrontSite } from '@/utils/operatorRequisites'

export type { StorefrontSite }

export type StorefrontItem = string | { text: string; sites?: StorefrontSite[] } | { paras: string[]; sites?: StorefrontSite[] }
export type StorefrontBlock =
  | { type: 'h2' | 'p'; text: string; id?: string }
  | { type: 'ul' | 'ol'; items: StorefrontItem[]; id?: string }
export interface StorefrontPage {
  path: '/contacts' | '/payment-info'
  title: string
  h1: string
  blocks: StorefrontBlock[]
}
export interface StorefrontPagesFile {
  contract: 'storefront-pages'
  version: string
  placeholders: string[]
  sites: StorefrontSite[]
  pages: { contacts: StorefrontPage; paymentInfo: StorefrontPage }
  footer: { identity: string; contacts: string }
  pricing: { notice: { zapis: string; derivedOrders: string }; termsUnderGrid: { blocks: StorefrontBlock[] }; messengerTaxNote: string }
  tariffs: Record<
    'zapisNetwork' | 'ordersChain' | 'zapisTrial',
    { description?: string; highlights: string[]; legacyDescription?: string; legacyHighlights: string[] }
  >
  faq: { zapisPriceAnswer: string }
}

export const STOREFRONT_PAGES = pagesJson as unknown as StorefrontPagesFile

export type InlineNode =
  | { kind: 'text'; value: string }
  | { kind: 'strong'; children: InlineNode[] }
  | { kind: 'em'; children: InlineNode[] }
  | { kind: 'link'; href: string; children: InlineNode[] }

export class StorefrontMarkupError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'StorefrontMarkupError'
  }
}

const HREF_RE = /^(\/(?!\/)|https:\/\/|tel:|mailto:)\S*$/

type Stop = '**' | '*' | ']' | null

/** Parses one line of mini-markup. `{{KEY}}` stays plain text here; the renderer fills it afterwards (a value with `*` cannot break the markup). */
export function parseInline(src: string): InlineNode[] {
  let pos = 0

  const seq = (stop: Stop): InlineNode[] => {
    const nodes: InlineNode[] = []
    let text = ''
    const flush = () => {
      if (text !== '') nodes.push({ kind: 'text', value: text })
      text = ''
    }
    for (;;) {
      if (pos >= src.length) {
        if (stop !== null) throw new StorefrontMarkupError(`unclosed "${stop}" in: ${src}`)
        flush()
        return nodes
      }
      if (src.startsWith('**', pos)) {
        if (stop === '**') {
          pos += 2
          flush()
          return nodes
        }
        pos += 2
        flush()
        nodes.push({ kind: 'strong', children: seq('**') })
        continue
      }
      const ch = src[pos]
      if (ch === '*') {
        if (stop === '*') {
          pos += 1
          flush()
          return nodes
        }
        pos += 1
        flush()
        nodes.push({ kind: 'em', children: seq('*') })
        continue
      }
      if (ch === '[') {
        pos += 1
        flush()
        const children = seq(']')
        if (src[pos] !== '(') throw new StorefrontMarkupError(`link text must be followed by "(href)" in: ${src}`)
        const close = src.indexOf(')', pos)
        if (close === -1) throw new StorefrontMarkupError(`unclosed "(" of a link in: ${src}`)
        const href = src.slice(pos + 1, close)
        if (!HREF_RE.test(href)) throw new StorefrontMarkupError(`href "${href}" is not allowed (only /, https://, tel:, mailto:) in: ${src}`)
        pos = close + 1
        nodes.push({ kind: 'link', href, children })
        continue
      }
      if (ch === ']') {
        if (stop === ']') {
          pos += 1
          flush()
          return nodes
        }
        throw new StorefrontMarkupError(`unexpected "]" in: ${src}`)
      }
      text += ch
      pos += 1
    }
  }

  return seq(null)
}

/** The item (string or object) rendered on `site`? An item without `sites` is shown everywhere. */
export function itemVisibleOn(item: StorefrontItem, site: StorefrontSite): boolean {
  if (typeof item === 'string') return true
  return item.sites === undefined || item.sites.includes(site)
}
