import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PricingPage } from './PricingPage'
import { useAuthStore } from '@/store/authStore'
import type { OrdersPublicPricingDto } from '../api/ordersPricing'
import { OPERATOR } from '@/utils/operatorRequisites'

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

describe('goods PricingPage (T38-14)', () => {
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

// Поправка витрины 31.10.2026: у каждой карточки своя кнопка; оплату они не обещают и ничего не подключают сами.
describe('goods PricingPage — plan card buttons', () => {
  it('guest: «Начать бесплатно» on the free plan, «Подключить» on paid ones — all to the registration of the bottom CTA', async () => {
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Лавка' })
    const free = screen.getByRole('link', { name: 'Начать бесплатно: тариф «Бесплатный»' })
    expect(free).toHaveTextContent('Начать бесплатно')
    expect(free).toHaveAttribute('href', '/register?returnTo=%2Fcabinet%2Fnew')
    for (const name of ['Лавка', 'Магазин', 'Сеть магазинов']) {
      const link = screen.getByRole('link', { name: `Подключить тариф «${name}»` })
      expect(link).toHaveTextContent('Подключить')
      expect(link).toHaveAttribute('href', '/register?returnTo=%2Fcabinet%2Fnew')
    }
    expect(screen.getByRole('link', { name: 'Подключить магазин' })).toBeInTheDocument()
  })
  it('signed in: paid plans lead to the subscription screen of the cabinet, the free one to the cabinet', async () => {
    useAuthStore.setState({ user: { id: 'u', roles: ['Client'] } as never, token: 't' })
    renderPage()
    await screen.findByRole('heading', { level: 3, name: 'Лавка' })
    expect(screen.getByRole('link', { name: 'Подключить тариф «Магазин»' })).toHaveAttribute('href', '/cabinet/subscription')
    expect(screen.getByRole('link', { name: 'Начать бесплатно: тариф «Бесплатный»' })).toHaveAttribute('href', '/cabinet')
  })
})

// CY48-10: the note under the grid is on the «Заказы» page too (ARCHITECTURE_CYCLE48 §48.8.2, п. 6.13.4).
describe('goods PricingPage — note under the grid (cycle 48)', () => {
  it('has the terms links on this domain and the tax reservation; the server notice stays', async () => {
    renderPage()
    expect(await screen.findByText('Заявка в кабинете.')).toBeInTheDocument()
    const note = await screen.findByTestId('pricing-terms-note')
    expect(within(note).getByRole('link', { name: 'Соглашении с компанией' })).toHaveAttribute('href', '/terms-owner')
    expect(within(note).getByRole('link', { name: 'Приложении № 1' })).toHaveAttribute('href', '/offer-channel')
    expect(within(note).getByRole('link', { name: 'Оплата и возврат' })).toHaveAttribute('href', '/payment-info')
    expect(within(note).getByRole('link', { name: 'Контакты и реквизиты' })).toHaveAttribute('href', '/contacts')
    expect(note.textContent).toContain(OPERATOR['НДС_ОГОВОРКА'])
  })
  it('no note without a grid', async () => {
    get.mockResolvedValue(null)
    renderPage()
    await screen.findByText('Тарифы пока не опубликованы.')
    expect(screen.queryByTestId('pricing-terms-note')).toBeNull()
  })
})
