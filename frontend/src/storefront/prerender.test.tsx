// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'
import { renderNoscript, renderPage } from './prerender'
import { OPERATOR, fillOperatorTemplate, telHref } from '@/utils/operatorRequisites'
import { STOREFRONT_PAGES, type StorefrontSite } from '@/utils/storefrontMarkup'

// CY48-11 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.10, §48.13.1): the strings that go into the release HTML.
const SITES: StorefrontSite[] = ['zapis', 'goods', 'dom', 'bani']
afterEach(() => vi.unstubAllEnvs())

const strip = (s: string) => s.replace(/<[^>]+>/g, '')

describe.each(SITES)('renderNoscript(%s)', (site) => {
  const html = renderNoscript(site)

  it('has the identity line, the contacts line and the document list', () => {
    const text = strip(html)
    expect(text).toContain(fillOperatorTemplate(STOREFRONT_PAGES.footer.identity))
    expect(html).toContain(`href="${telHref()}"`)
    expect(html).toContain(`href="mailto:${OPERATOR['ПОЧТА_ПОДДЕРЖКИ']}"`)
    expect(html).toContain('href="/contacts"')
    expect(html).toContain('href="/payment-info"')
    for (const href of ['/terms-owner', '/offer-channel', '/privacy', '/terms'])
      expect(html).toContain(`href="${href}"`)
  })

  it('«Тарифы и цены» only where /pricing exists', () => {
    const has = html.includes('Тарифы и цены')
    expect(has).toBe(site === 'zapis' || site === 'goods')
  })

  it('no inline style, no script, no placeholder, no API address', () => {
    expect(html).not.toMatch(/\sstyle=/)
    expect(html).not.toContain('<script')
    expect(html).not.toContain('{{')
    expect(html).not.toContain('/api/')
  })
})

describe.each(SITES)('renderPage(%s)', (site) => {
  it.each([
    ['contacts', 'Контакты и реквизиты — EZBOOK'],
    ['paymentInfo', 'Оплата и возврат — EZBOOK'],
  ] as const)('%s: title, one h1, INN, nothing unfilled', (page, title) => {
    const result = renderPage(site, page)
    expect(result.title).toBe(title)
    expect(result.html.match(/<h1[\s>]/g)).toHaveLength(1)
    expect(result.html).toContain(OPERATOR['ИНН_ОПЕРАТОРА'])
    expect(result.html).not.toContain('{{')
    expect(result.html).not.toContain('/api/')
    expect(result.html).not.toMatch(/\sstyle=/)
    expect(result.html).not.toContain('<script')
  })

  it('the dom and bani pages link «Тарифы и цены» to the «Записи» site', () => {
    vi.stubEnv('DEV', false) // as in the release build: production addresses
    const html = renderPage(site, 'paymentInfo').html
    const expected = site === 'dom' || site === 'bani' ? 'https://ezbook.ru/pricing' : '/pricing'
    expect(html).toContain(`href="${expected}"`)
  })
})
