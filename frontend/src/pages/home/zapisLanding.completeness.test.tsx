import { describe, it, expect, vi } from 'vitest'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServiceLanding } from '../../components/landing/ServiceLanding'
import { collectConfigStrings } from '../../components/landing/testConfig'
import type { PricingLine } from '../../components/pricing/pricingLine'
import { zapisLanding } from './zapisLanding'

// CY46-09 (K7): компактная раскладка цикла 46 ничего не потеряла — каждая строка конфига есть на странице.
const line: PricingLine = {
  queryKey: ['public-pricing', 'completeness'],
  fetchGrid: vi.fn().mockResolvedValue({
    plans: [{ id: 'p', name: 'План', description: null, pricePerMonth: 690, highlights: [], sortOrder: 1, isFree: false, isTrial: false, limitLines: [] }],
    options: [],
    notice: '',
    legalNotice: null,
  }),
}

describe('«Запись»: everything from the config is on the page (CY46-09)', () => {
  it('texts, img alts and aria-labels', async () => {
    const config = { ...zapisLanding, pricing: zapisLanding.pricing && { ...zapisLanding.pricing, line } }
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const catalog = <div>каталог-заглушка</div>
    const { container } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <ServiceLanding config={config} catalog={catalog} />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    await vi.waitFor(() => expect(container.querySelector('#pricing-title') !== null || !config.pricing).toBe(true))
    const page = container.textContent ?? ''
    const { text, alt, ariaLabel } = collectConfigStrings(config)
    expect(text.length).toBeGreaterThan(10)
    const missing = text.filter((t) => !page.includes(t))
    expect(missing).toEqual([])
    const imgAlts = Array.from(container.querySelectorAll('img')).map((i) => i.getAttribute('alt'))
    expect(alt.filter((a) => !imgAlts.includes(a))).toEqual([])
    const labels = Array.from(container.querySelectorAll('[aria-label]')).map((e) => e.getAttribute('aria-label'))
    expect(ariaLabel.filter((a) => !labels.includes(a))).toEqual([])
  })
})
