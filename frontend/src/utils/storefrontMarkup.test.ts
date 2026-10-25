// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { itemVisibleOn, parseInline, STOREFRONT_PAGES, StorefrontMarkupError, type StorefrontBlock, type StorefrontItem } from './storefrontMarkup'

// CY48-06: the mini-markup parser.
describe('parseInline', () => {
  it('plain text and placeholders stay text', () => {
    expect(parseInline('ИНН {{ИНН_ОПЕРАТОРА}}')).toEqual([{ kind: 'text', value: 'ИНН {{ИНН_ОПЕРАТОРА}}' }])
  })

  it('bold, italic and a link with a bold part', () => {
    expect(parseInline('a **b** *c* [d **e**](/x)')).toEqual([
      { kind: 'text', value: 'a ' },
      { kind: 'strong', children: [{ kind: 'text', value: 'b' }] },
      { kind: 'text', value: ' ' },
      { kind: 'em', children: [{ kind: 'text', value: 'c' }] },
      { kind: 'text', value: ' ' },
      {
        kind: 'link',
        href: '/x',
        children: [
          { kind: 'text', value: 'd ' },
          { kind: 'strong', children: [{ kind: 'text', value: 'e' }] },
        ],
      },
    ])
  })

  it('a link inside bold; a placeholder inside href', () => {
    expect(parseInline('**[t](tel:{{ТЕЛЕФОН_TEL}})**')).toEqual([
      { kind: 'strong', children: [{ kind: 'link', href: 'tel:{{ТЕЛЕФОН_TEL}}', children: [{ kind: 'text', value: 't' }] }] },
    ])
  })

  it('parentheses in plain text are not a link', () => {
    expect(parseInline('п. 4.1 (см. ниже)')).toEqual([{ kind: 'text', value: 'п. 4.1 (см. ниже)' }])
  })

  it('HTML is not interpreted: it stays text', () => {
    expect(parseInline('<b>x</b><script>')).toEqual([{ kind: 'text', value: '<b>x</b><script>' }])
  })

  it.each(['**open', '*open', '[text](/x', '[text] (/x)', '[text', 'stray ] bracket'])('unclosed or broken construct throws: %s', (src) => {
    expect(() => parseInline(src)).toThrow(StorefrontMarkupError)
  })

  it.each(['javascript:alert(1)', 'http://insecure.example', '//evil.example', 'ftp://x', 'relative/path'])('foreign href %s throws', (href) => {
    expect(() => parseInline(`[x](${href})`)).toThrow(StorefrontMarkupError)
  })

  it.each(['/pricing', 'https://npd.nalog.ru/check-status/', 'tel:+79130000000', 'mailto:a@b.ru'])('allowed href %s', (href) => {
    expect(parseInline(`[x](${href})`)).toEqual([{ kind: 'link', href, children: [{ kind: 'text', value: 'x' }] }])
  })
})

describe('itemVisibleOn', () => {
  it('a string and an item without sites are visible everywhere; sites restrict', () => {
    const plain: StorefrontItem = 'x'
    const limited: StorefrontItem = { text: 'y', sites: ['zapis', 'goods'] }
    expect(itemVisibleOn(plain, 'bani')).toBe(true)
    expect(itemVisibleOn(limited, 'goods')).toBe(true)
    expect(itemVisibleOn(limited, 'dom')).toBe(false)
  })
})

describe('storefront-pages.json parses', () => {
  const strings: string[] = []
  const walkBlocks = (blocks: StorefrontBlock[]) => {
    for (const b of blocks) {
      if (b.type === 'h2' || b.type === 'p') strings.push(b.text)
      else
        for (const it of b.items) {
          if (typeof it === 'string') strings.push(it)
          else if ('paras' in it) strings.push(...it.paras)
          else strings.push(it.text)
        }
    }
  }
  walkBlocks(STOREFRONT_PAGES.pages.contacts.blocks)
  walkBlocks(STOREFRONT_PAGES.pages.paymentInfo.blocks)
  walkBlocks(STOREFRONT_PAGES.pricing.termsUnderGrid.blocks)
  strings.push(STOREFRONT_PAGES.footer.identity, STOREFRONT_PAGES.footer.contacts)

  it('every string of every block is valid markup', () => {
    expect(strings.length).toBeGreaterThan(20)
    for (const s of strings) expect(() => parseInline(s), s).not.toThrow()
  })

  it('every block type and every `sites` value is allowed', () => {
    const okSites = new Set(STOREFRONT_PAGES.sites)
    const check = (blocks: StorefrontBlock[]) => {
      for (const b of blocks) {
        expect(['h2', 'p', 'ul', 'ol']).toContain(b.type)
        if (b.type === 'ul' || b.type === 'ol')
          for (const it of b.items) if (typeof it !== 'string') for (const s of it.sites ?? []) expect(okSites.has(s)).toBe(true)
      }
    }
    check(STOREFRONT_PAGES.pages.contacts.blocks)
    check(STOREFRONT_PAGES.pages.paymentInfo.blocks)
    check(STOREFRONT_PAGES.pricing.termsUnderGrid.blocks)
  })
})
