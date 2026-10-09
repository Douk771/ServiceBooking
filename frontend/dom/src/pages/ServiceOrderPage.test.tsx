import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServiceOrderPage } from './ServiceOrderPage'
import { httpError } from '../test/fixtures'
import type { PublicServiceOrderDto } from '../types'

const api = vi.hoisted(() => ({ get: vi.fn(), uploadProof: vi.fn(), proofBlob: vi.fn(), cancel: vi.fn(), pushSubscribe: vi.fn(), pushUnsubscribe: vi.fn() }))
vi.mock('../api/serviceOrders', () => ({ serviceOrdersApi: api }))
vi.mock('../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

const order = (over: Partial<PublicServiceOrderDto> = {}): PublicServiceOrderDto => ({
  status: 'Confirmed',
  displayStatus: 'Confirmed',
  statusText: 'Подтверждено',
  serverTimeUtc: '2027-01-14T10:00:00Z',
  service: { name: 'Баня', url: '/kedr-park/uslugi/banya', coverUrl: null },
  company: { name: 'Кедр Парк', phone: '+79001112233', url: '/kedr-park', address: 'ул. Лесная, 5', yandexMapsUrl: null, twoGisUrl: null },
  provider: { status: 'IndividualEntrepreneur', statusLabel: 'Индивидуальный предприниматель', name: 'ИП Иванов', inn: '123456789012', ogrn: null, claimsAddress: 'Новокузнецк' },
  time: { businessDate: '2027-01-15', startMinute: 1320, endMinute: 1500, hours: 3, startUtc: '2027-01-15T15:00:00Z', endUtc: '2027-01-15T18:00:00Z', label: 'пт 15 янв, 22:00 — сб 16 янв, 01:00' },
  items: [],
  lines: [{ kind: 'Service', label: 'Баня · 3 ч', quantity: 1, unitPriceRub: 6000, amountRub: 6000 }],
  hourPrices: [],
  serviceAmountRub: 6000,
  itemsAmountRub: 0,
  totalRub: 6000,
  prepayPercent: 30,
  prepayRub: 1800,
  dueOnSiteRub: 4200,
  payment: null,
  paymentProofs: [],
  proofs: { canAttach: false, maxCount: 3, maxBytes: 10485760, acceptedTypes: ['application/pdf'] },
  cancellation: {
    policy: 'PreparationCosts',
    summary: 'Расходы на подготовку: отмена не позднее чем за 12 ч до начала — вся предоплата',
    canCancel: true,
    refund: { kind: 'CostsOnlyUpTo', refundAtLeastRub: null, maxDeductionRub: 1800, text: 'Компания вправе удержать только фактические расходы на подготовку, не больше 1 800 ₽. Остальное она обязана вернуть' },
  },
  guestName: 'Анна',
  guestPhoneMasked: '+7 900 *** ** 33',
  notifications: { webPush: { available: false }, messengerSelected: false },
  availableActions: ['Cancel'],
  ...over,
})

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/s/tok']}>
        <Routes>
          <Route path="/s/:token" element={<ServiceOrderPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => Object.values(api).forEach((f) => f.mockReset()))

describe('ServiceOrderPage', () => {
  it('shows the status, the time with TWO calendar dates and no tourist tax (ЮР39-8, Т39-15)', async () => {
    api.get.mockResolvedValue(order())
    renderPage()
    expect(await screen.findByTestId('order-status')).toHaveTextContent('Подтверждено')
    expect(screen.getByTestId('session-time')).toHaveTextContent('пт 15 янв, 22:00 — сб 16 янв, 01:00')
    const text = document.body.textContent ?? ''
    expect(text).not.toMatch(/туристическ/i)
    expect(text.toLowerCase()).not.toContain('бизнес')
  })

  it('the cancel dialog shows the server refund text as is, says the company makes the refund, and never «не меньше 0 ₽» (ЮР39-1)', async () => {
    api.get.mockResolvedValue(order())
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Отменить сеанс' }))
    const refund = await screen.findByTestId('refund-text')
    expect(refund).toHaveTextContent('только фактические расходы на подготовку, не больше 1 800 ₽')
    expect(document.body.textContent).not.toMatch(/не меньше 0/)
    expect(screen.getByText(/Возврат делает компания, а не сервис/)).toBeInTheDocument()
    expect(within(screen.getByRole('dialog')).getByRole('link', { name: '+7 (900) 111-22-33' })).toBeInTheDocument()
  })

  it('replaces the screen with the order of a 409 «CancelNotAllowed» and prints its text', async () => {
    api.get.mockResolvedValue(order())
    const started = order({ displayStatus: 'Confirmed', cancellation: { ...order().cancellation, canCancel: false, cannotCancelText: 'Сеанс уже начался — по вопросам свяжитесь с компанией: +7 (900) 111-22-33' } })
    api.cancel.mockRejectedValue(httpError(409, { code: 'CancelNotAllowed', message: 'Сеанс уже начался — по вопросам свяжитесь с компанией: +7 (900) 111-22-33', order: started }))
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Отменить сеанс' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Отменить сеанс' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Сеанс уже начался')
    await waitFor(() => expect(api.cancel).toHaveBeenCalledTimes(1))
  })

  it('a held order shows the timer and the payment details of THIS order, the status line stays in words', async () => {
    api.get.mockResolvedValue(
      order({
        status: 'Held',
        displayStatus: 'Held',
        statusText: 'Удержан — ожидает оплаты',
        holdExpiresAtUtc: '2027-01-14T10:30:00Z',
        payment: { details: 'Сбербанк, карта 2202', purpose: 'Баня 15 янв', amountRub: 1800 },
        proofs: { canAttach: true, maxCount: 3, maxBytes: 10485760, acceptedTypes: ['application/pdf'] },
      }),
    )
    renderPage()
    expect(await screen.findByTestId('order-status')).toHaveTextContent('Удержан — ожидает оплаты')
    expect(screen.getByRole('timer')).toBeInTheDocument()
    expect(within(screen.getByTestId('payment-details')).getByText(/Сбербанк, карта 2202/)).toBeInTheDocument()
    expect(screen.getByText('Приложить подтверждение оплаты')).toBeInTheDocument()
  })

  it('shows «not found» for an unknown token and an error with retry for a failed load', async () => {
    api.get.mockRejectedValueOnce(httpError(404, ''))
    renderPage()
    expect(await screen.findByText('Заказ не найден')).toBeInTheDocument()
  })

  it('shows an error with a retry button when the server is down', async () => {
    api.get.mockRejectedValue(httpError(500, ''))
    renderPage()
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервер временно недоступен')
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
