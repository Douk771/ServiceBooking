import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServiceLanding } from './ServiceLanding'
import { makeConfig, shot, steps } from './testConfig'
import type { LandingConfig, LandingStepsPanel } from './types'
import type { PricingGridView, PricingLine } from '../pricing/pricingLine'

const grid: PricingGridView = {
  plans: [{ id: 'p', name: 'План', description: null, pricePerMonth: 690, highlights: [], sortOrder: 1, isFree: false, isTrial: false, limitLines: [] }],
  options: [],
  notice: '',
  legalNotice: null,
}
const line: PricingLine = { queryKey: ['public-pricing', 'test'], fetchGrid: vi.fn() }

function renderLanding(config: LandingConfig) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ServiceLanding config={config} catalog={<div data-testid="svc-catalog">каталог</div>} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}
const before = (a: Element, b: Element) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)

beforeEach(() => {
  vi.mocked(line.fetchGrid).mockReset().mockResolvedValue(grid)
})

describe('ServiceLanding order (T37-01)', () => {
  it('renders sections in fixed order with one h1 and no footer', async () => {
    const { container } = renderLanding(makeConfig({ pricing: { line, lead: 'Лид' } }))
    await screen.findByText(/Лид — от/)
    const ids = ['cat', 'c-title', 'b-title', 'pricing-title', 'faq']
    const els = [container.querySelector('h1')!, ...ids.map((id) => container.querySelector(`#${id}`)!)]
    els.forEach((e) => expect(e).toBeTruthy())
    for (let i = 0; i < els.length - 1; i++) expect(before(els[i], els[i + 1])).toBe(true)
    expect(container.querySelectorAll('h1')).toHaveLength(1)
    expect(container.querySelector('main')).toBeTruthy()
    expect(container.querySelector('footer')).toBeNull()
    expect(screen.getByTestId('svc-catalog').closest('#cat')).toHaveAttribute('aria-label', 'Каталог')
    for (const id of ['c-title', 'b-title', 'pricing-title', 'faq-title']) expect(container.querySelector(`#${id}`)!.tagName).toBe('H2')
  })

  it('omits pricing when not configured; media sits between catalog and clients only if configured', () => {
    const none = renderLanding(makeConfig())
    expect(none.container.querySelector('#pricing-title')).toBeNull()
    expect(none.container.querySelector('figure img')).toBeNull()
    none.unmount()
    const { container } = renderLanding(makeConfig({ media: { src: '/m.jpg', alt: 'Интерьер', width: 800, height: 400 } }))
    const img = screen.getByAltText('Интерьер')
    expect(img).toHaveAttribute('loading', 'lazy')
    expect(img).toHaveAttribute('width', '800')
    expect(before(container.querySelector('#cat')!, img)).toBe(true)
    expect(before(img, container.querySelector('#c-title')!)).toBe(true)
  })

  it('hides pricing when the grid is missing (404)', async () => {
    vi.mocked(line.fetchGrid).mockResolvedValue(null)
    const { container } = renderLanding(makeConfig({ pricing: { line, lead: 'Лид' } }))
    await screen.findByText('Частые вопросы')
    await Promise.resolve()
    expect(container.querySelector('#pricing-title')).toBeNull()
  })
})

describe('screenshot placeholder (T37-02)', () => {
  const PH = 'landing-screenshot-placeholder'
  it('one placeholder inside section 3 when both slots are empty', () => {
    const { container } = renderLanding(makeConfig())
    const all = screen.getAllByTestId(PH)
    expect(all).toHaveLength(1)
    expect(all[0].closest('section')).toBe(container.querySelector('[aria-labelledby="c-title"]'))
    expect(screen.getByText('Здесь будут скриншоты')).toBeInTheDocument()
    expect(all[0].tagName).toBe('DIV')
  })
  it('none when both screenshots are present', () => {
    const base = makeConfig()
    renderLanding({ ...base, clients: { ...base.clients, screenshot: shot }, business: { ...base.business, screenshot: shot } })
    expect(screen.queryByTestId(PH)).toBeNull()
    expect(screen.getAllByRole('img')).toHaveLength(2)
  })
  it('placeholder moves to section 4 when only section 3 has a screenshot', () => {
    const base = makeConfig()
    const { container } = renderLanding({ ...base, clients: { ...base.clients, screenshot: shot } })
    expect(screen.getByTestId(PH).closest('section')).toBe(container.querySelector('[aria-labelledby="b-title"]'))
  })
})

describe('section class parity (T37-03)', () => {
  it('sections 3 and 4 share wrapper, heading and steps classes', () => {
    const { container } = renderLanding(makeConfig())
    const s3 = container.querySelector('[aria-labelledby="c-title"]')!
    const s4 = container.querySelector('[aria-labelledby="b-title"]')!
    expect(s3.className).toBe(s4.className)
    const cls = (root: Element, sel: string) => root.querySelector(sel)!.className
    expect(cls(s3, '#c-steps')).toBe(cls(s4, '#b-steps'))
    expect(cls(s3, 'ol')).toBe(cls(s4, 'ol'))
    expect(cls(s3, 'ol li div')).toBe(cls(s4, 'ol li div'))
    expect(cls(s3, 'ol li h4')).toBe(cls(s4, 'ol li h4'))
    expect(cls(s3, '#c-title').replace(/ scroll-mt-24 focus:outline-none/, '')).toBe(cls(s4, '#b-title'))
    expect(cls(s3, '#c-steps').length).toBeGreaterThan(0)
  })
})

describe('compile-time shape (T37-05)', () => {
  it('rejects wrong step/faq counts and missing title via tsc', () => {
    const ok = steps('x')
    // @ts-expect-error — две шага вместо трёх
    const two: LandingStepsPanel = { title: 't', titleId: 'i', steps: [ok[0], ok[1]] }
    // @ts-expect-error — четыре шага вместо трёх
    const four: LandingStepsPanel = { title: 't', titleId: 'i', steps: [...ok, ok[0]] }
    // @ts-expect-error — нет title
    const noTitle: LandingStepsPanel = { titleId: 'i', steps: ok }
    // @ts-expect-error — FAQ из пяти пунктов
    const faq5: LandingConfig['faq'] = { items: [{ question: 'q', answer: 'a' }, { question: 'q', answer: 'a' }, { question: 'q', answer: 'a' }, { question: 'q', answer: 'a' }, { question: 'q', answer: 'a' }] }
    expect([two, four, noTitle, faq5]).toHaveLength(4)
  })
})
