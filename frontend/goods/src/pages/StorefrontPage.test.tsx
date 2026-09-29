import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { StorefrontPage } from './StorefrontPage'
import { cartStorageKey } from '../utils/cart'
import type { StorefrontDto } from '../types'

const get = vi.fn()
vi.mock('../api/storefront', () => ({ storefrontApi: { get: (...a: unknown[]) => get(...a), quote: () => new Promise(() => {}), createOrder: vi.fn() } }))
vi.mock('@/api/phoneVerification', () => ({ phoneVerificationApi: { getConfig: () => Promise.resolve({ enabled: false, healthy: false }) } }))
vi.mock('../api/legalNotice', () => ({ legalNoticeApi: { orderCheckout: () => new Promise(() => {}) } }))

const dto = (over: Partial<StorefrontDto> = {}): StorefrontDto => ({
  slug: 'shaurma', name: 'Шаурма на Ленина', publicUrl: 'https://goods.ezbook.ru/shaurma', description: 'Свежая', address: 'ул. Ленина, 12', cityName: 'Барнаул', phone: '79001234567',
  isAvailable: true, acceptingOrders: true, customerMode: 'Anyone', allowCustomerCancel: true, seller: null,
  categories: [
    { id: 'c1', name: 'Горячее', products: [
      { id: 'p1', name: 'Шаурма классическая', unit: 'Piece', price: 250, minQuantity: 1, maxQuantity: 99, foodInfo: { compositionAndAllergens: 'Курица, глютен' }, available: true },
      { id: 'p3', name: 'Пирожок', unit: 'Piece', price: 60, minQuantity: 1, maxQuantity: 99, foodInfo: {}, available: false },
    ] },
    { id: null, name: 'Другое', products: [
      { id: 'p2', name: 'Сыр твёрдый', unit: 'Weight', price: 540, weightStepGrams: 100, minQuantity: 200, maxQuantity: 10000, foodInfo: {}, available: true },
    ] },
  ],
  ...over,
}) as StorefrontDto

function renderPage(path = '/shaurma') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/:slug" element={<StorefrontPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const stored = () => JSON.parse(window.localStorage.getItem(cartStorageKey('shaurma')) ?? 'null')

beforeEach(() => {
  window.localStorage.clear()
  get.mockReset().mockResolvedValue(dto())
})

describe('StorefrontPage', () => {
  it('renders categories, «Другое» last, prices per kg and the composition', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Шаурма на Ленина' })).toBeInTheDocument()
    const headings = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)
    expect(headings).toEqual(['Горячее', 'Другое'])
    expect(screen.getByText('540 ₽/кг')).toBeInTheDocument()
    expect(screen.getByText('Состав и аллергены')).toBeInTheDocument()
  })

  it('adds to the cart, keeps it in localStorage per shop and shows the running total (≈ for weight)', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Шаурма на Ленина' })
    await user.click(within(screen.getAllByTestId('product-card')[0]).getByRole('button', { name: 'В корзину' }))
    await user.click(screen.getByRole('button', { name: 'Добавить ещё: Шаурма классическая' }))
    expect(stored()).toEqual([{ productId: 'p1', quantity: 2, unitPriceSeen: 250 }])
    expect(screen.getByTestId('cart-bar-total')).toHaveTextContent('500 ₽')

    // a weighed product starts at its minimum (200 g) and makes the total approximate
    await user.click(within(screen.getAllByTestId('product-card')[2]).getByRole('button', { name: 'В корзину' }))
    expect(stored()).toContainEqual({ productId: 'p2', quantity: 200, unitPriceSeen: 540 })
    expect(screen.getByTestId('cart-bar-total')).toHaveTextContent('≈ 608 ₽')
  })

  it('does not let an unavailable product into the cart — only «Закончилось»', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Шаурма на Ленина' })
    const card = screen.getAllByTestId('product-card')[1]
    expect(within(card).getByText('Закончилось')).toBeInTheDocument()
    expect(within(card).queryByRole('button', { name: 'В корзину' })).toBeNull()
  })

  it('restores the saved cart after a reload', async () => {
    window.localStorage.setItem(cartStorageKey('shaurma'), JSON.stringify([{ productId: 'p1', quantity: 3, unitPriceSeen: 250 }]))
    renderPage()
    expect(await screen.findByTestId('cart-bar-total')).toHaveTextContent('750 ₽')
  })

  it('opens the cart straight away on ?checkout=1 (return from /login)', async () => {
    window.localStorage.setItem(cartStorageKey('shaurma'), JSON.stringify([{ productId: 'p1', quantity: 1, unitPriceSeen: 250 }]))
    renderPage('/shaurma?checkout=1')
    expect(await screen.findByRole('dialog', { name: 'Ваш заказ' })).toBeInTheDocument()
  })

  it('warns when the shop is not accepting orders', async () => {
    get.mockResolvedValue(dto({ acceptingOrders: false, notAcceptingReason: 'Сейчас закрыто' }))
    renderPage()
    expect(await screen.findByText('Сейчас закрыто')).toBeInTheDocument()
  })

  it('shows «Магазин недоступен» for a blocked shop and a 404 page for an unknown address', async () => {
    get.mockResolvedValue(dto({ isAvailable: false, acceptingOrders: false, categories: [] }))
    const first = renderPage()
    expect(await screen.findByRole('heading', { name: 'Магазин недоступен' })).toBeInTheDocument()
    first.unmount()

    get.mockRejectedValue({ response: { status: 404, data: '' } })
    renderPage('/nonexistent')
    expect(await screen.findByRole('heading', { name: 'Магазин не найден' })).toBeInTheDocument()
  })

  it('offers a retry when loading fails', async () => {
    get.mockRejectedValue({ response: { status: 500, data: '' } })
    renderPage()
    expect(await screen.findByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })

  it('prints the seller block only when filled', async () => {
    get.mockResolvedValue(dto({ seller: { legalForm: 'Ip', legalName: 'ИП Иванов', inn: '123456789012' } }))
    renderPage()
    expect(await screen.findByText(/ИП, ИП Иванов, ИНН 123456789012/)).toBeInTheDocument()
  })
})
