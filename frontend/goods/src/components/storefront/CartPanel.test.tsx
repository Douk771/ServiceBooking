import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'
import { CartPanel } from './CartPanel'
import { useCart } from '../../hooks/useCart'
import { cartStorageKey } from '../../utils/cart'
import type { QuoteDto, StorefrontDto, StorefrontProductDto } from '../../types'

const quote = vi.fn()
const createOrder = vi.fn()

vi.mock('../../api/storefront', () => ({
  storefrontApi: { quote: (...a: unknown[]) => quote(...a), createOrder: (...a: unknown[]) => createOrder(...a) },
}))
vi.mock('../../api/legalNotice', () => ({ legalNoticeApi: { orderCheckout: () => Promise.reject(Object.assign(new Error('404'), { response: { status: 404 } })) } }))
vi.mock('@/api/profile', () => ({ profileApi: { get: () => Promise.resolve({ phoneVerified: false }) } }))
vi.mock('@/api/phoneVerification', () => ({ phoneVerificationApi: { getConfig: () => Promise.resolve({ enabled: false, healthy: false }) } }))

const product = (over: Partial<StorefrontProductDto> = {}): StorefrontProductDto => ({
  id: 'p1', name: 'Шаурма классическая', unit: 'Piece', price: 250, minQuantity: 1, maxQuantity: 99, foodInfo: {}, available: true, ...over,
})
const shop = (over: Partial<StorefrontDto> = {}): StorefrontDto =>
  ({ slug: 'shaurma', name: 'Шаурма', publicUrl: 'https://goods.ezbook.ru/shaurma', isAvailable: true, acceptingOrders: true, customerMode: 'Anyone', allowCustomerCancel: true, categories: [], ...over }) as StorefrontDto

const quoteFor = (unitPrice: number, over: Partial<QuoteDto> = {}): QuoteDto => ({
  lines: [{ productId: 'p1', name: 'Шаурма классическая', unit: 'Piece', unitPrice, quantity: 2, lineTotal: unitPrice * 2, isApproximate: false }],
  total: unitPrice * 2, isApproximate: false, hasProblems: false, acceptingOrders: true, ...over,
})

function Harness({ shopDto = shop() }: { shopDto?: StorefrontDto }) {
  const cart = useCart('shaurma')
  const products = new Map([['p1', product()]])
  return <CartPanel slug="shaurma" shop={shopDto} products={products} cart={cart} onClose={() => {}} />
}

function renderPanel(shopDto?: StorefrontDto) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/shaurma']}>
        <Routes>
          <Route path="/shaurma" element={<Harness shopDto={shopDto} />} />
          <Route path="/o/:token" element={<div>Страница заказа</div>} />
          <Route path="/login" element={<div>Страница входа</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const seedCart = (seen = 250) => window.localStorage.setItem(cartStorageKey('shaurma'), JSON.stringify([{ productId: 'p1', quantity: 2, unitPriceSeen: seen }]))

const created = { order: { token: 'tok', number: 27 }, orderUrl: `${window.location.origin}/o/tok` }

beforeEach(() => {
  window.localStorage.clear()
  quote.mockReset().mockResolvedValue(quoteFor(250))
  createOrder.mockReset()
  useAuthStore.setState({ user: null, token: null })
})
afterEach(() => useAuthStore.setState({ user: null, token: null }))

async function fillGuest(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Имя *'), 'Иван')
  await user.type(screen.getByLabelText('Телефон *'), '+79001234567')
}

describe('CartPanel — checkout', () => {
  it('sends the seen price, the guest phone and one idempotency key; clears the cart and opens the order on success', async () => {
    seedCart()
    createOrder.mockResolvedValue(created)
    const user = userEvent.setup()
    renderPanel()
    await fillGuest(user)
    await user.click(await screen.findByRole('button', { name: /Заказать/ }))
    await screen.findByText('Страница заказа')
    expect(createOrder).toHaveBeenCalledTimes(1)
    const [slug, body] = createOrder.mock.calls[0]
    expect(slug).toBe('shaurma')
    expect(body).toMatchObject({ customerName: 'Иван', customerPhone: '79001234567', items: [{ productId: 'p1', quantity: 2, expectedUnitPrice: 250 }] })
    expect(body.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/)
    expect(window.localStorage.getItem(cartStorageKey('shaurma'))).toBeNull()
  })

  it('a double tap creates one order', async () => {
    seedCart()
    createOrder.mockImplementation(() => new Promise((r) => setTimeout(() => r(created), 50)))
    const user = userEvent.setup()
    renderPanel()
    await fillGuest(user)
    const btn = await screen.findByRole('button', { name: /Заказать/ })
    await user.dblClick(btn)
    await screen.findByText('Страница заказа')
    expect(createOrder).toHaveBeenCalledTimes(1)
  })

  it('blocks ordering until a changed price is accepted, then orders at the new price', async () => {
    seedCart(250)
    quote.mockResolvedValue(quoteFor(270))
    createOrder.mockResolvedValue(created)
    const user = userEvent.setup()
    renderPanel()
    await fillGuest(user)
    expect(await screen.findByText(/Цена изменилась: было 250 ₽, стало 270 ₽/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Заказать/ })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Принять новые цены' }))
    const order = await screen.findByRole('button', { name: /Заказать/ })
    await waitFor(() => expect(order).toBeEnabled())
    await user.click(order)
    await screen.findByText('Страница заказа')
    expect(createOrder.mock.calls[0][1].items[0].expectedUnitPrice).toBe(270)
  })

  it('keeps the cart and lists every problem when the server refuses; a retry reuses the same key', async () => {
    seedCart()
    createOrder
      .mockRejectedValueOnce({ response: { status: 409, data: { code: 'ItemsUnavailable', message: 'Некоторые товары недоступны', problems: [{ productId: 'p1', name: 'Шаурма классическая', reason: 'InsufficientStock', message: 'Осталось только 1 шт', availableQuantity: 1 }] } } })
      .mockResolvedValueOnce(created)
    const user = userEvent.setup()
    renderPanel()
    await fillGuest(user)
    await user.click(await screen.findByRole('button', { name: /Заказать/ }))
    const banner = await screen.findByTestId('order-refusal')
    expect(banner).toHaveAttribute('data-code', 'ItemsUnavailable')
    expect(banner).toHaveTextContent('Шаурма классическая: Осталось только 1 шт')
    expect(window.localStorage.getItem(cartStorageKey('shaurma'))).not.toBeNull()
    await user.click(screen.getByRole('button', { name: /Заказать/ }))
    await screen.findByText('Страница заказа')
    expect(createOrder.mock.calls[1][1].idempotencyKey).toBe(createOrder.mock.calls[0][1].idempotencyKey)
  })

  it('does not call the server when the guest phone is missing', async () => {
    seedCart()
    const user = userEvent.setup()
    renderPanel()
    await user.type(await screen.findByLabelText('Имя *'), 'Иван')
    await user.click(screen.getByRole('button', { name: /Заказать/ }))
    expect(await screen.findByText('Укажите телефон')).toBeInTheDocument()
    expect(createOrder).not.toHaveBeenCalled()
  })

  it('blocks ordering while a line has a problem and while the shop does not accept orders', async () => {
    seedCart()
    quote.mockResolvedValue(quoteFor(250, { hasProblems: true, lines: [{ ...quoteFor(250).lines[0], problem: { productId: 'p1', name: 'Шаурма', reason: 'SoldOut', message: 'Закончилось' } }] }))
    renderPanel()
    expect(await screen.findByText('Закончилось')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Заказать/ })).toBeDisabled()
  })
})

describe('CartPanel — strict mode (VerifiedPhoneOnly)', () => {
  it('sends a guest to /login with returnTo back to the shop and shows no order form', async () => {
    seedCart()
    const user = userEvent.setup()
    renderPanel(shop({ customerMode: 'VerifiedPhoneOnly' }))
    expect(await screen.findByText(/только от покупателей с подтверждённым телефоном/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Имя *')).toBeNull()
    const login = screen.getByRole('link', { name: 'Войти' })
    expect(login).toHaveAttribute('href', '/login?returnTo=%2Fshaurma%3Fcheckout%3D1')
    await user.click(login)
    expect(await screen.findByText('Страница входа')).toBeInTheDocument()
    expect(window.localStorage.getItem(cartStorageKey('shaurma'))).not.toBeNull() // the cart survives the round trip
  })

  it('says ordering is impossible when the phone is unconfirmed and the confirmation subsystem is off', async () => {
    seedCart()
    useAuthStore.setState({ user: { id: 'u', phone: '79001234567', firstName: 'И', lastName: 'П', roles: ['Client'] }, token: 't' })
    renderPanel(shop({ customerMode: 'VerifiedPhoneOnly' }))
    expect(await screen.findByText(/Подтверждение телефона сейчас недоступно/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Заказать/ })).toBeNull()
  })
})

describe('CartPanel — empty state', () => {
  it('shows the empty cart instead of a checkout form', async () => {
    renderPanel()
    expect(await screen.findByText('Корзина пуста')).toBeInTheDocument()
    expect(quote).not.toHaveBeenCalled()
  })
})
