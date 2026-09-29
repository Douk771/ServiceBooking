import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { StorefrontPage } from './StorefrontPage'
import { cartStorageKey } from '../utils/cart'
import { pickupStorageKey } from '../utils/pickup'
import type { StorefrontDto } from '../types'

const get = vi.fn()
const pickupSlots = vi.fn()
vi.mock('../api/storefront', () => ({ storefrontApi: { get: (...a: unknown[]) => get(...a), pickupSlots: (...a: unknown[]) => pickupSlots(...a), quote: () => new Promise(() => {}), createOrder: vi.fn() } }))
vi.mock('@/api/phoneVerification', () => ({ phoneVerificationApi: { getConfig: () => Promise.resolve({ enabled: false, healthy: false }) } }))
vi.mock('../api/legalNotice', () => ({
  legalNoticeApi: { orderCheckout: () => new Promise(() => {}) },
  orderLegalTextsApi: { messengerConsent: () => new Promise(() => {}), preorderNotice: () => Promise.reject({ response: { status: 404 } }) },
}))

const dto = (over: Partial<StorefrontDto> = {}): StorefrontDto => ({
  slug: 'shaurma', name: 'Шаурма на Ленина', publicUrl: 'https://goods.ezbook.ru/shaurma', description: 'Свежая', address: 'ул. Ленина, 12', cityName: 'Барнаул', phone: '79001234567',
  isAvailable: true, acceptingOrders: true, customerMode: 'Anyone', allowCustomerCancel: true, seller: null,
  date: '2026-09-30', openState: { isOpen: true, text: 'Открыто до 21:00' }, workingHours: { lines: [{ dayLabel: 'пн–пт', text: '09:00–21:00' }] },
  pickup: { asapEnabled: true, scheduledEnabled: true, asap: { available: true, text: '≈ к 13:20' }, dates: [{ date: '2026-09-30', label: 'Сегодня', hasSlots: true }, { date: '2026-10-01', label: 'Завтра', hasSlots: true }, { date: '2026-10-02', label: 'пт 2 окт', hasSlots: false, reasonText: 'Сегодня уже не успеем приготовить — выберите другой день' }] },
  customerNotifications: { webPushOffered: false, messengerOffered: false },
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
  pickupSlots.mockReset().mockResolvedValue({ date: '2026-10-01', label: 'Завтра', slots: [{ startUtc: '2026-10-01T06:00:00Z', endUtc: '2026-10-01T06:15:00Z', label: '09:00–09:15' }, { startUtc: '2026-10-01T06:15:00Z', endUtc: '2026-10-01T06:30:00Z', label: '09:15–09:30' }] })
})

describe('StorefrontPage', () => {
  it('renders categories, «Другое» last, prices per kg and the composition', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Шаурма на Ленина' })).toBeInTheDocument()
    const headings = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent?.trim())
    expect(headings).toEqual(['Когда заберёте', 'Горячее', 'Другое'])
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

  it('shows the open state in words and the working hours, all from the server', async () => {
    renderPage()
    expect(await screen.findByText('Открыто до 21:00')).toBeInTheDocument()
    expect(screen.getByText('пн–пт')).toBeInTheDocument()
  })

  it('shows the server reason code text when not accepting and keeps the assortment visible', async () => {
    get.mockResolvedValue(dto({ acceptingOrders: false, notAcceptingCode: 'Paused', notAcceptingReason: 'Магазин временно не принимает заказы — до 13:30' }))
    renderPage()
    const banner = await screen.findByTestId('not-accepting')
    expect(banner).toHaveTextContent('Магазин временно не принимает заказы — до 13:30')
    expect(banner).toHaveAttribute('data-code', 'Paused')
    expect(screen.getAllByTestId('product-card').length).toBeGreaterThan(0)
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

  describe('pick-up time (cycle 24)', () => {
    const chosen = () => JSON.parse(window.localStorage.getItem(pickupStorageKey('shaurma')) ?? 'null')

    it('preselects «как можно скорее» once when the shop can do it now, showing the server text', async () => {
      renderPage()
      const picker = await screen.findByTestId('pickup-picker')
      expect(within(picker).getByRole('radio', { name: 'Как можно скорее' })).toHaveAttribute('aria-checked', 'true')
      expect(within(picker).getByTestId('asap-text')).toHaveTextContent('≈ к 13:20')
      await waitFor(() => expect(chosen().choice).toEqual({ kind: 'Asap' }))
    })

    it('does not preselect when «как можно скорее» is impossible now, and says why with the server text', async () => {
      get.mockResolvedValue(dto({ pickup: { ...dto().pickup, asap: { available: false, text: 'Сегодня уже не успеем приготовить заказ' } } }))
      renderPage()
      const picker = await screen.findByTestId('pickup-picker')
      expect(within(picker).getByRole('radio', { name: 'Как можно скорее' })).toBeDisabled()
      expect(chosen()).toBeNull()
    })

    it('picking a date requests that date\'s assortment (?date=) and picking a slot stores it with the server labels', async () => {
      const user = userEvent.setup()
      renderPage()
      const picker = await screen.findByTestId('pickup-picker')
      await user.click(within(picker).getByRole('radio', { name: 'К определённому времени' }))
      await user.click(await within(picker).findByRole('radio', { name: 'Завтра' }))
      await waitFor(() => expect(get).toHaveBeenLastCalledWith('shaurma', '2026-10-01'))
      await user.click(await within(picker).findByRole('radio', { name: '09:15–09:30' }))
      expect(chosen().choice).toEqual({ kind: 'Slot', date: '2026-10-01', slotStartUtc: '2026-10-01T06:15:00Z', dateLabel: 'Завтра', slotLabel: '09:15–09:30' })
      expect(pickupSlots).toHaveBeenCalledWith('shaurma', '2026-10-01')
    })

    it('a date without slots is disabled and its reason is printed', async () => {
      const user = userEvent.setup()
      renderPage()
      const picker = await screen.findByTestId('pickup-picker')
      await user.click(within(picker).getByRole('radio', { name: 'К определённому времени' }))
      expect(within(picker).getByRole('radio', { name: 'пт 2 окт' })).toBeDisabled()
      expect(within(picker).getByText(/пт 2 окт: Сегодня уже не успеем приготовить/)).toBeInTheDocument()
    })

    it('moves between slots with the arrow keys (radiogroup) and selects as it moves', async () => {
      const user = userEvent.setup()
      renderPage()
      const picker = await screen.findByTestId('pickup-picker')
      await user.click(within(picker).getByRole('radio', { name: 'К определённому времени' }))
      await user.click(await within(picker).findByRole('radio', { name: 'Завтра' }))
      const first = await within(picker).findByRole('radio', { name: '09:00–09:15' })
      first.focus()
      await user.keyboard('{ArrowRight}')
      expect(within(picker).getByRole('radio', { name: '09:15–09:30' })).toHaveAttribute('aria-checked', 'true')
      expect(chosen().choice.slotStartUtc).toBe('2026-10-01T06:15:00Z')
    })

    it('drops a saved slot the server no longer lists and says so', async () => {
      window.localStorage.setItem(pickupStorageKey('shaurma'), JSON.stringify({ choice: { kind: 'Slot', date: '2026-10-01', slotStartUtc: '2026-10-01T05:00:00Z' }, browseDate: '2026-10-01' }))
      renderPage()
      expect(await screen.findByTestId('pickup-notice')).toHaveTextContent('Это время уже недоступно — выберите другое')
      expect(chosen()?.choice ?? null).toBeNull()
    })

    it('when the requested date is refused, shows the server notice and returns to today', async () => {
      window.localStorage.setItem(pickupStorageKey('shaurma'), JSON.stringify({ choice: null, browseDate: '2026-10-12' }))
      get.mockImplementation((_slug: string, date?: string) => Promise.resolve(date ? dto({ dateNotice: 'На 12 окт заказать нельзя — показан ассортимент на сегодня' }) : dto()))
      renderPage()
      expect(await screen.findByTestId('pickup-notice')).toHaveTextContent('На 12 окт заказать нельзя — показан ассортимент на сегодня')
      await waitFor(() => expect(get).toHaveBeenLastCalledWith('shaurma', undefined))
    })

    it('announces the shown assortment date to screen readers (aria-live)', async () => {
      renderPage()
      await screen.findByTestId('pickup-picker')
      await waitFor(() => expect(screen.getByTestId('assortment-live')).toHaveTextContent('Показан ассортимент на Сегодня'))
      expect(screen.getByTestId('assortment-live')).toHaveAttribute('aria-live', 'polite')
    })
  })
})
