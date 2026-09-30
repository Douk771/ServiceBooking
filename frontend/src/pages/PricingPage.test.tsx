import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PricingPage } from './PricingPage'
import type { PublicPricingDto, PricingPlanDto } from '../types/pricing'

// Cycle 28 (FE-4, API_CONTRACT_CYCLE28.md §601): the page is fed the five-plan catalog exactly as the contract describes it
// after `ops tariffs apply` + publication (free, trial, Студия, Салон, Сеть; `options: []`).

const getPublicPricing = vi.fn()
vi.mock('../api/pricing', () => ({ pricingApi: { getPublicPricing: (...a: unknown[]) => getPublicPricing(...a) } }))

function plan(over: Partial<PricingPlanDto> & Pick<PricingPlanDto, 'id' | 'name' | 'sortOrder' | 'pricePerMonth'>): PricingPlanDto {
  return {
    description: null,
    highlights: [],
    includedCompanies: 1,
    includedEmployees: 1,
    isFree: false,
    ...over,
  }
}

const CATALOG: PublicPricingDto = {
  version: 'v1',
  currency: 'RUB',
  notice: 'Цены указаны в рублях.',
  options: [],
  plans: [
    // deliberately out of order: the page sorts by sortOrder
    plan({ id: 'network', name: 'Сеть', sortOrder: 40, pricePerMonth: 3900, includedCompanies: null, includedEmployees: null, highlights: ['Итоговая цена — после заявки, по числу филиалов'] }),
    plan({ id: 'trial', name: 'Пробный период', sortOrder: 10, pricePerMonth: 0, isTrial: true, includedCompanies: 3, includedEmployees: 15, highlights: ['14 дней тарифа «Салон»'] }),
    plan({ id: 'salon', name: 'Салон', sortOrder: 30, pricePerMonth: 1890, includedCompanies: 3, includedEmployees: 15 }),
    plan({ id: 'free', name: 'Бесплатный', sortOrder: -1, pricePerMonth: 0, isFree: true }),
    plan({ id: 'studio', name: 'Студия', sortOrder: 20, pricePerMonth: 790, includedCompanies: 1, includedEmployees: 5 }),
  ],
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <PricingPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const card = (name: string) => screen.getByRole('heading', { level: 3, name }).parentElement as HTMLElement

beforeEach(() => {
  getPublicPricing.mockReset().mockResolvedValue(CATALOG)
})

describe('PricingPage — five-plan catalog (cycle 28)', () => {
  it('renders all five plans in sortOrder with the grid prices', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Студия' })

    const names = screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)
    expect(names).toEqual(['Бесплатный', 'Пробный период', 'Студия', 'Салон', 'Сеть'])
    expect(within(card('Студия')).getByText(/790\s₽\/мес/)).toBeInTheDocument()
    expect(within(card('Салон')).getByText(/1\s890\s₽\/мес/)).toBeInTheDocument()
    expect(within(card('Сеть')).getByText(/3\s900\s₽\/мес/)).toBeInTheDocument()
  })

  it('the trial carries its badge and is a separate card from the free plan', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Пробный период' })

    // The badge and the heading are both "Пробный период": one span badge + one heading inside the same card.
    const trialCard = card('Пробный период')
    expect(within(trialCard).getAllByText('Пробный период')).toHaveLength(2)
    expect(within(card('Бесплатный')).queryByText('Пробный период')).not.toBeInTheDocument()
  })

  it('«Сеть» (null limits) says WHAT is unlimited, not two bare «без ограничений»', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Сеть' })

    const network = card('Сеть')
    expect(within(network).getByText('Компании без ограничений')).toBeInTheDocument()
    expect(within(network).getByText('Сотрудники без ограничений')).toBeInTheDocument()
    expect(within(card('Студия')).getByText('до 1 компании')).toBeInTheDocument()
  })

  it('the accent goes to the first paid plan (Студия), not to the trial that follows the free plan', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Студия' })

    expect(card('Студия')).toHaveClass('bg-ink')
    expect(card('Пробный период')).not.toHaveClass('bg-ink')
    expect(card('Бесплатный')).not.toHaveClass('bg-ink')
  })

  it('no options in the catalog → no «Дополнительные опции» block', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Студия' })
    expect(screen.queryByText('Дополнительные опции')).not.toBeInTheDocument()
  })
})

describe('PricingPage — states', () => {
  it('404 (publication is off) is a plain notice, not an error', async () => {
    getPublicPricing.mockResolvedValue(null)
    renderPage()
    expect(await screen.findByText('Тарифы пока не опубликованы.')).toBeInTheDocument()
  })

  it('a failed request shows a retry button', async () => {
    getPublicPricing.mockRejectedValue(new Error('500'))
    renderPage()
    expect(await screen.findByRole('button', { name: 'Попробовать снова' })).toBeInTheDocument()
  })

  it('the tab title names the product EZBOOK, not the internal project name', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Студия' })
    expect(document.title).toBe('Тарифы и цены — EZBOOK')
  })
})
