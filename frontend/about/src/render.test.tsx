import { describe, it, expect } from 'vitest'
import { renderSite } from './prerender'
import { PRODUCTS } from './content/products'
import { PAGE_COPY } from './content/page'
import type { ProductCard, ProductKey } from './content/types'
import { DEMO_SITE_URLS, PRODUCTION_SITE_URLS, SiteUrlError } from '@/utils/publicSites'
import { OPERATOR, telHref } from '@/utils/operatorRequisites'

// CY44-02 (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.14): renderSite on status fixtures. Pure function, no browser.
const TEMPLATE = '<!doctype html><html lang="ru"><head><!--about:head--></head><body><!--about:body--></body></html>'
const base = { template: TEMPLATE, env: {}, mode: 'production' as const, year: 2026 }

function withStatuses(statuses: Record<ProductKey, 'live' | 'testing' | 'hidden'>): ProductCard[] {
  return PRODUCTS.map((p) => {
    const status = statuses[p.key]
    const note = 'Тестовая заметка'
    if (status === 'live') return { ...p, status, businessAction: p.businessAction ?? { label: 'Для бизнеса', path: '/#biz-title' }, testingNote: undefined } as ProductCard
    if (status === 'testing') return { ...p, status, businessAction: undefined, testingNote: p.testingNote ?? note } as ProductCard
    return { ...p, status } as ProductCard
  })
}
// Cycle 48: the footer requisites line has tel:/mailto: links; they are not «external site» links.
const isContactHref = (h: string) => h.startsWith('tel:') || h.startsWith('mailto:')
const cards = (html: string) => [...html.matchAll(/data-product="([a-z]+)"/g)].map((m) => m[1])
const actionLinks = (html: string, action: string) =>
  (html.match(new RegExp(`<a [^>]*data-action="${action}"[^>]*>`, 'g')) ?? []).map((a) => /href="([^"]+)"/.exec(a)![1])

describe('CY44-02 renderSite', () => {
  it('customer defaults (20.10.2026): all four live, nothing hidden, demo links for zapis and goods', () => {
    const { files, report } = renderSite(base)
    const html = files['index.html']
    expect(cards(html)).toEqual(['zapis', 'goods', 'dom', 'bani'])
    expect(report.visibleProducts).toEqual(['zapis', 'goods', 'dom', 'bani'])
    expect(report.hiddenProducts).toEqual([])
    expect(report.hiddenHosts).toEqual([])
    expect((html.match(/data-action="business"/g) ?? []).length).toBe(4)
    expect(actionLinks(html, 'demo')).toEqual([
      'https://demo.visit.ezbook.ru/',
      'https://demo.zakaz.ezbook.ru/',
    ])
    expect(html).not.toContain('data-role="testing-badge"')
  })

  it('a hidden product is nowhere, including its host', () => {
    const { files, report } = renderSite({ ...base, products: withStatuses({ zapis: 'live', goods: 'live', dom: 'live', bani: 'hidden' }) })
    const html = files['index.html']
    expect(report.hiddenHosts).toEqual(['bani.ezbook.ru'])
    for (const host of report.hiddenHosts) expect(html).not.toContain(host)
    expect(html).not.toContain('#product-bani')
    expect(html).not.toContain('EZBOOK Бани')
  })

  it('testing card: badge text, no business action and no call to join', () => {
    const html = renderSite({ ...base, products: withStatuses({ zapis: 'live', goods: 'live', dom: 'testing', bani: 'hidden' }) }).files['index.html']
    const dom = html.slice(html.indexOf('data-product="dom"'), html.indexOf('</article>', html.indexOf('data-product="dom"')))
    expect(dom).toContain('data-role="testing-badge"')
    expect(dom).toContain('Тестовый режим')
    expect(dom).not.toContain('data-action="business"')
    expect(dom).not.toMatch(/Для бизнеса|подключ|попробуй|зарегистр/i)
    expect((html.match(/data-action="business"/g) ?? []).length).toBe(2)
  })

  it('all live: four cards, four business actions', () => {
    const html = renderSite({ ...base, products: withStatuses({ zapis: 'live', goods: 'live', dom: 'live', bani: 'live' }) }).files['index.html']
    expect(cards(html)).toEqual(['zapis', 'goods', 'dom', 'bani'])
    expect((html.match(/data-action="business"/g) ?? []).length).toBe(4)
    expect(html).toContain('href="https://bani.ezbook.ru/#biz-title"')
  })

  it('all hidden except one: the hidden ones are in no place (cards, nav, footer, chooser, hosts)', () => {
    const html = renderSite({ ...base, products: withStatuses({ zapis: 'hidden', goods: 'live', dom: 'hidden', bani: 'hidden' }) }).files['index.html']
    expect(cards(html)).toEqual(['goods'])
    for (const hidden of ['zapis.', 'EZBOOK Запись', 'EZBOOK Дома', 'EZBOOK Бани', 'dom.ezbook.ru', 'bani.ezbook.ru', 'demo.visit.ezbook.ru', '#product-zapis', '#product-dom']) {
      expect(html, hidden).not.toContain(hidden)
    }
  })

  it('the demo stand of a hidden product is in hiddenHosts (prerender self-check)', () => {
    const { report } = renderSite({ ...base, products: withStatuses({ zapis: 'live', goods: 'hidden', dom: 'live', bani: 'live' }) })
    expect(report.hiddenHosts).toEqual(['goods.ezbook.ru', 'demo.zakaz.ezbook.ru'])
  })

  it('every action link: no target, screen-reader context with the product name, 44px', () => {
    const html = renderSite(base).files['index.html']
    const links = [...html.matchAll(/<a [^>]*data-action="[a-z]+"[^>]*>[\s\S]*?<\/a>/g)].map((m) => m[0])
    expect(links.length).toBe(10)
    for (const a of links) {
      expect(a).not.toMatch(/\starget=/)
      expect(a).toMatch(/<span class="sr-only"> — EZBOOK [А-Яа-я]+<\/span><\/a>$/)
      expect(a).toContain('min-h-[44px]')
    }
  })

  it('one h1, lang, canonical, description, title, og; no script, style attr, noindex, root div, /api/', () => {
    const html = renderSite(base).files['index.html']
    expect((html.match(/<h1[\s>]/g) ?? []).length).toBe(1)
    expect(html).toContain('lang="ru"')
    expect(html).toContain('<link rel="canonical" href="https://about.ezbook.ru/"/>')
    expect(html).toContain(`<title>${PAGE_COPY.meta.title}</title>`)
    expect(html).toContain('<meta name="description"')
    expect(html).toContain('property="og:url" content="https://about.ezbook.ru/"')
    for (const bad of ['<script', ' style=', 'noindex', '<div id="root">', '/api/', '<!--about:']) expect(html, bad).not.toContain(bad)
  })

  it('every h3 belongs to a card; nav links only to visible cards', () => {
    const html = renderSite(base).files['index.html']
    expect((html.match(/<h3 /g) ?? []).length).toBe(4)
    const nav = html.slice(html.indexOf('<nav'), html.indexOf('</nav>'))
    expect([...nav.matchAll(/href="#product-([a-z]+)"/g)].map((m) => m[1])).toEqual(['zapis', 'goods', 'dom', 'bani'])
  })

  it('external links are absolute and belong to the site addresses', () => {
    const html = renderSite(base).files['index.html']
    const origins = new Set([...Object.values(PRODUCTION_SITE_URLS), ...Object.values(DEMO_SITE_URLS)])
    const hrefs = [...html.matchAll(/ href="([^"]+)"/g)].map((m) => m[1]).filter((h) => !h.startsWith('#') && !h.startsWith('/') && !isContactHref(h))
    expect(hrefs.length).toBeGreaterThan(5)
    for (const h of hrefs) expect(origins.has(new URL(h).origin), h).toBe(true)
    expect(html).toContain('href="https://ezbook.ru/privacy"')
    expect(html).toContain('href="https://ezbook.ru/#biz-title"')
    expect(html).toContain('href="https://goods.ezbook.ru/"')
  })

  it('uses VITE_SITE_URL_* overrides', () => {
    const { files } = renderSite({ ...base, env: { VITE_SITE_URL_ABOUT: 'https://example.org', VITE_SITE_URL_GOODS: 'https://g.example.org' } })
    expect(files['index.html']).toContain('href="https://example.org/"')
    expect(files['index.html']).toContain('href="https://g.example.org/#biz-title"')
    expect(files['robots.txt']).toContain('Sitemap: https://example.org/sitemap.xml')
  })

  it('escapes text from the copy', () => {
    const copy = { ...PAGE_COPY, hero: { title: '<b>"x"</b> & y', subtitle: 's' } }
    const html = renderSite({ ...base, copy }).files['index.html']
    expect(html).not.toContain('<b>')
    expect(html).toContain('&lt;b&gt;')
  })

  it('robots.txt, sitemap.xml and 404.html', () => {
    const { files } = renderSite(base)
    expect(files['robots.txt']).toBe('User-agent: *\nAllow: /\n\nSitemap: https://about.ezbook.ru/sitemap.xml\n')
    expect(files['sitemap.xml']).toContain('<loc>https://about.ezbook.ru/</loc>')
    expect((files['sitemap.xml'].match(/<loc>/g) ?? []).length).toBe(1)
    const nf = files['404.html']
    expect(nf).toContain('<meta name="robots" content="noindex"/>')
    expect(nf).not.toContain('rel="canonical"')
    expect(nf).toContain('<title>Страница не найдена — EZBOOK</title>')
    expect(nf).toContain('href="/"')
    expect(nf).not.toContain('<script')
    expect((nf.match(/<h1[\s>]/g) ?? []).length).toBe(1)
  })

  it('throws on a bad address and on a template without markers', () => {
    expect(() => renderSite({ ...base, env: { VITE_SITE_URL_ABOUT: 'about.ezbook.ru' } })).toThrow(SiteUrlError)
    expect(() => renderSite({ ...base, template: '<html></html>' })).toThrow('about template: marker <!--about:head--> not found')
  })
})

// CY48-12 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.6.4, A48-8): the requisites lines in the about footer, links absolute to the «Записи» site.
describe('CY48-12 requisites in the about footer', () => {
  it('shows the identity line and absolute /contacts and /payment-info links; stays script-free', () => {
    const html = renderSite(base).files['index.html']
    expect(html).toContain(`ИНН <span class="whitespace-nowrap">${OPERATOR['ИНН_ОПЕРАТОРА']}</span>`)
    expect(html).toContain(`href="${PRODUCTION_SITE_URLS.zapis}/contacts"`)
    expect(html).toContain(`href="${PRODUCTION_SITE_URLS.zapis}/payment-info"`)
    expect(html).toContain(`href="${telHref()}"`)
    expect(html).not.toContain('<script')
    expect(html).not.toContain('style=')
  })
})
