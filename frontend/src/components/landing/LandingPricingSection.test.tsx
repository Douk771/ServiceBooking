import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { LandingPricingSection } from './LandingPricingSection'
import type { PricingGridView, PricingLine, PricingPlanView } from '../pricing/pricingLine'

const plan = (o: Partial<PricingPlanView> & Pick<PricingPlanView, 'id' | 'name' | 'sortOrder' | 'pricePerMonth'>): PricingPlanView => ({
  description: null, highlights: [], isFree: false, isTrial: false, limitLines: [], ...o,
})
const fetchGrid = vi.fn()
const line: PricingLine = { queryKey: ['public-pricing', 'test'], fetchGrid }
const grid = (plans: PricingPlanView[]): PricingGridView => ({ plans, options: [], notice: '', legalNotice: null })

function renderIt() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}><MemoryRouter><LandingPricingSection config={{ line, lead: 'Приём заказов' }} /></MemoryRouter></QueryClientProvider>)
}
beforeEach(() => {
  fetchGrid.mockReset()
})

describe('LandingPricingSection (T37-08)', () => {
  it('renders nothing while loading', () => {
    fetchGrid.mockReturnValue(new Promise(() => {}))
    expect(renderIt().container).toBeEmptyDOMElement()
  })
  it.each([
    ['404', () => Promise.resolve(null)],
    ['error', () => Promise.reject(new Error('boom')) as Promise<null>],
    ['empty list', () => Promise.resolve(grid([]))],
  ])('renders nothing on %s', async (_n, impl) => {
    fetchGrid.mockImplementation(impl)
    const { container } = renderIt()
    await waitFor(() => expect(fetchGrid).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 10))
    expect(container).toBeEmptyDOMElement()
  })
  it('shows lead with the cheapest paid price, max 3 cards in sortOrder, limit lines and link', async () => {
    fetchGrid.mockResolvedValue(grid([
      plan({ id: 'd', name: 'Четвёртый', sortOrder: 40, pricePerMonth: 2990 }),
      plan({ id: 'a', name: 'Бесплатный', sortOrder: 10, pricePerMonth: 0, isFree: true, limitLines: ['до 1 магазина'] }),
      plan({ id: 'b', name: 'Лавка', sortOrder: 20, pricePerMonth: 690, limitLines: ['до 1 магазина', 'до 1 500 заказов в месяц'], highlights: ['h1', 'h2', 'h3', 'h4'] }),
      plan({ id: 'c', name: 'Магазин', sortOrder: 30, pricePerMonth: 1490 }),
    ]))
    renderIt()
    await screen.findByText('Приём заказов — от 690 ₽/мес')
    expect(screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['Бесплатный', 'Лавка', 'Магазин'])
    expect(screen.getByText('до 1 500 заказов в месяц')).toBeInTheDocument()
    expect(screen.getByText('h3')).toBeInTheDocument()
    expect(screen.queryByText('h4')).toBeNull()
    expect(screen.getByRole('link', { name: /Все тарифы/ })).toHaveAttribute('href', '/pricing')
  })
  it('omits the price line when there is no paid plan', async () => {
    fetchGrid.mockResolvedValue(grid([plan({ id: 'a', name: 'Бесплатный', sortOrder: 1, pricePerMonth: 0, isFree: true })]))
    renderIt()
    await screen.findByText('Тарифы')
    expect(screen.queryByText(/— от/)).toBeNull()
  })
})
