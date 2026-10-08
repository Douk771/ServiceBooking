import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PricingPage } from './PricingPage'
import { useAuthStore } from '@/store/authStore'
import type { OrdersPublicPricingDto } from '../api/ordersPricing'

const get = vi.fn()
vi.mock('../api/ordersPricing', () => ({ ordersPricingApi: { get: (...a: unknown[]) => get(...a) } }))

const p = (id: string, name: string, sortOrder: number, price: number, isFree = false) => ({
  id, name, description: null, pricePerMonth: price, highlights: [], includedShops: 1, includedMembers: 5,
  includedProductsPerShop: 300, includedOrdersPerMonth: 1500, sortOrder, isFree,
})
const DTO: OrdersPublicPricingDto = {
  version: 'v', currency: 'RUB', notice: 'Заявка в кабинете.', legalNotice: null,
  plans: [p('c', 'Сеть магазинов', 40, 2990), p('s', 'Магазин', 30, 1490), p('l', 'Лавка', 20, 690), p('f', 'Бесплатный', 10, 0, true)],
}

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter><PricingPage /></MemoryRouter>
    </QueryClientProvider>,
  )

beforeEach(() => {
  get.mockReset().mockResolvedValue(DTO)
  useAuthStore.setState({ user: null, token: null })
})

describe('goods PricingPage (T37-14)', () => {
  it('four plans by sortOrder, document title', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Лавка' })
    const names = screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)
    expect(names).toEqual(['Бесплатный', 'Лавка', 'Магазин', 'Сеть магазинов'])
    expect(document.title).toBe('Тарифы и цены — EZBOOK Заказы')
  })
  it('404 shows the not-published notice', async () => {
    get.mockResolvedValue(null)
    renderPage()
    expect(await screen.findByText('Тарифы пока не опубликованы.')).toBeInTheDocument()
  })
  it('CTA depends on sign-in', async () => {
    const { unmount } = renderPage()
    expect(await screen.findByRole('link', { name: 'Подключить магазин' })).toHaveAttribute('href', '/register?returnTo=%2Fcabinet%2Fnew')
    unmount()
    useAuthStore.setState({ user: { id: 'u', roles: ['Client'] } as never, token: 't' })
    renderPage()
    expect(await screen.findByRole('link', { name: 'Подключить магазин' })).toHaveAttribute('href', '/cabinet/new')
  })
})
