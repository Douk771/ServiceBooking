import { describe, it, expect, vi } from 'vitest'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServiceLanding } from '@/components/landing/ServiceLanding'
import { stayLanding } from './stayLanding'

// QA CY46-02/03/K8 (US-46-02, US-46-06): у «Домов» порядок прежний (файлы по сервисам — ESLint-границы goods/dom/bani):
// герой -> каталог -> клиенты -> «для бизнеса» (#biz-title) -> тарифы -> FAQ; шаги внутри бизнес-секции.
vi.mock('@/api/pricing', () => ({ pricingApi: { getPublicPricing: vi.fn().mockResolvedValue(null) } }))

const follows = (a: Element, b: Element) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)

describe.each([
  ['Дома', stayLanding],
])('%s: порядок блоков не изменился', (_name, config) => {
  it('h1 -> каталог -> клиенты -> #biz-title -> #faq; шаги внутри бизнес-секции', () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <ServiceLanding config={config} catalog={<div id="stub-cat">каталог</div>} heroAside={<form aria-label="Панель" />} />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    const catalogEl = container.querySelector(`#${config.catalog.id}`) ?? container.querySelector('#stub-cat')!
    const seq = [container.querySelector('h1')!, catalogEl, container.querySelector(`#${config.clients.titleId}`)!, container.querySelector('#biz-title')!, container.querySelector('#faq-title')!]
    seq.forEach((e) => expect(e).not.toBeNull())
    for (let i = 1; i < seq.length; i++) expect(follows(seq[i - 1], seq[i])).toBe(true)
    expect(container.querySelectorAll('h1')).toHaveLength(1)
  })
})
