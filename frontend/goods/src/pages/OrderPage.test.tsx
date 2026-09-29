import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { OrderPage } from './OrderPage'
import type { PublicOrderDto } from '../types'

const getPublic = vi.fn()
const cancelPublic = vi.fn()
vi.mock('../hooks/useOrderPush', () => ({ useOrderPush: () => ({ reason: null, subscribed: false, busy: false, error: null, enable: () => {}, disable: () => {} }) }))
vi.mock('../api/orders', () => ({ ordersApi: { getPublic: (...a: unknown[]) => getPublic(...a), cancelPublic: (...a: unknown[]) => cancelPublic(...a) } }))

const timeline = (reached: number) =>
  (['New', 'Accepted', 'Ready', 'Issued'] as const).map((status, i) => ({ status, title: ['Новый', 'Принят', 'Готов к выдаче', 'Выдан'][i], reached: i < reached, reachedAtUtc: i < reached ? '2026-10-05T10:00:00Z' : null }))

const order = (over: Partial<PublicOrderDto> = {}): PublicOrderDto => ({
  token: 'tok', number: 27, businessDate: '2026-10-05', createdAtUtc: '2026-10-05T10:00:00Z', status: 'New', statusText: 'Новый', timeline: timeline(1),
  items: [{ name: 'Сыр твёрдый', unit: 'Weight', unitPrice: 540, quantityOrdered: 540, lineTotal: 291.6, isApproximate: true }],
  total: 291.6, totalIsApproximate: true, comment: 'без лука', customerName: 'Иван', customerPhoneMasked: '+7 (900) ***-**-67', canCancel: true,
  shop: { name: 'Шаурма', slug: 'shaurma', publicUrl: 'https://goods.ezbook.ru/shaurma', phone: '79001234567', address: 'ул. Ленина, 12' },
  shopChanges: [], version: 1, isGuest: true,
  pickup: { kind: 'Asap', date: '2026-10-05', startUtc: '2026-10-05T10:15:00Z', dueUtc: '2026-10-05T10:30:00Z', text: 'Как можно скорее (≈ 13:15)', isPreorder: false, isOverdue: false },
  notifications: { webPush: { available: false, publicKey: null, unavailableText: null }, messengerRequested: false }, ...over,
}) as PublicOrderDto

function renderPage(state?: unknown) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[{ pathname: '/o/tok', state }]}>
        <Routes>
          <Route path="/o/:token" element={<OrderPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getPublic.mockReset().mockResolvedValue(order())
  cancelPublic.mockReset()
})
afterEach(() => vi.useRealTimers())

describe('OrderPage', () => {
  it('shows number, status, timeline, ≈ total, masked phone and the shop', async () => {
    renderPage()
    expect(await screen.findByTestId('order-number')).toHaveTextContent('№ 27')
    expect(screen.getByRole('list', { name: 'Ход выполнения заказа' })).toBeInTheDocument()
    expect(screen.getByTestId('order-total')).toHaveTextContent('≈ 291,60 ₽')
    expect(screen.getByText(/сумма уточнится|Сумма уточнится/)).toBeInTheDocument()
    expect(screen.getByText(/\+7 \(900\) \*\*\*-\*\*-67/)).toBeInTheDocument()
    expect(screen.queryByText(/79001234567$/)).toBeNull() // the buyer's phone is only ever the masked form
    expect(screen.getByRole('link', { name: 'Шаурма' })).toHaveAttribute('href', '/shaurma')
  })

  it('tells a guest to save the link and offers to copy it', async () => {
    renderPage({ justCreated: true })
    expect(await screen.findByText('Сохраните ссылку: другого способа вернуться к заказу нет.')).toBeInTheDocument()
    expect(screen.getByText('Заказ оформлен')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Скопировать/ })).toBeInTheDocument()
  })

  it('highlights what the shop changed, with the comment', async () => {
    getPublic.mockResolvedValue(order({ shopChanges: [{ occurredAtUtc: '2026-10-05T10:20:00Z', comment: 'Сыра нет', changes: [{ name: 'Сыр', text: 'Сыр: 500 г → 300 г' }], totalBefore: 270, totalAfter: 162 }] }))
    renderPage()
    expect(await screen.findByText('Магазин изменил заказ')).toBeInTheDocument()
    expect(screen.getByText('Сыр: 500 г → 300 г')).toBeInTheDocument()
    expect(screen.getByText('«Сыра нет»')).toBeInTheDocument()
  })

  it('a final status shows the outcome with the reason instead of the timeline', async () => {
    getPublic.mockResolvedValue(order({ status: 'Rejected', statusText: 'Отклонён', reason: 'Нет товара', canCancel: false }))
    renderPage()
    expect(await screen.findByText('Причина: Нет товара')).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Ход выполнения заказа' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Отменить заказ' })).toBeNull()
  })

  it('polls every 10 s while the order is active and stops on a final status', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    getPublic
      .mockResolvedValueOnce(order())
      .mockResolvedValueOnce(order({ status: 'Ready', statusText: 'Готов к выдаче', timeline: timeline(3) }))
      .mockResolvedValue(order({ status: 'Issued', statusText: 'Выдан', timeline: timeline(4), canCancel: false }))
    renderPage()
    await screen.findByTestId('order-number')
    expect(getPublic).toHaveBeenCalledTimes(1)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_100)
    })
    await waitFor(() => expect(getPublic).toHaveBeenCalledTimes(2))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_100)
    })
    await waitFor(() => expect(getPublic).toHaveBeenCalledTimes(3))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30_000)
    })
    expect(getPublic).toHaveBeenCalledTimes(3) // Issued is final — no more requests
  })

  it('cancels after confirmation', async () => {
    cancelPublic.mockResolvedValue(order({ status: 'CancelledByCustomer', statusText: 'Отменён покупателем', canCancel: false }))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Отменить заказ' }))
    expect(screen.getByRole('dialog', { name: 'Отменить заказ № 27?' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Да, отменить' }))
    expect(await screen.findByText('Отменён покупателем', { selector: 'p' })).toBeInTheDocument()
    expect(cancelPublic).toHaveBeenCalledWith('tok')
  })

  it('when the order is already ready, shows the server message and the fresh order from the body', async () => {
    cancelPublic.mockRejectedValue({
      response: { status: 409, data: { code: 'AlreadyReady', message: 'Заказ уже собран — свяжитесь с магазином', publicOrder: order({ status: 'Ready', statusText: 'Готов к выдаче', timeline: timeline(3), canCancel: false }) } },
    })
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Отменить заказ' }))
    await user.click(screen.getByRole('button', { name: 'Да, отменить' }))
    expect(await screen.findByText('Заказ уже собран — свяжитесь с магазином')).toBeInTheDocument()
    expect(screen.getByText('Готов к выдаче', { selector: 'span' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Отменить заказ' })).toBeNull()
  })

  it('erased personal data are not printed as nulls', async () => {
    getPublic.mockResolvedValue(order({ customerName: null, customerPhoneMasked: null }))
    renderPage()
    expect(await screen.findByText('Данные покупателя удалены')).toBeInTheDocument()
  })

  it('an unknown link is a plain 404 page — nothing hints at whether the order existed', async () => {
    getPublic.mockRejectedValue({ response: { status: 404, data: '' } })
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Заказ не найден' })).toBeInTheDocument()
  })
  describe('cycle 24: pick-up time and notifications', () => {
    it('shows the pick-up text from the server prominently, with «Предзаказ» for a future date', async () => {
      getPublic.mockResolvedValue(order({ pickup: { kind: 'Slot', date: '2026-10-09', startUtc: '2026-10-09T09:30:00Z', dueUtc: '2026-10-09T09:30:00Z', text: 'пт 9 окт, к 12:30', isPreorder: true, isOverdue: false } }))
      renderPage()
      expect(await screen.findByTestId('order-pickup')).toHaveTextContent('пт 9 окт, к 12:30')
      expect(screen.getByTestId('order-pickup')).toHaveTextContent('Предзаказ')
    })

    it('says a message will come only when it was requested and the order is still active', async () => {
      getPublic.mockResolvedValue(order({ notifications: { webPush: { available: false }, messengerRequested: true } }))
      renderPage()
      expect(await screen.findByTestId('order-messenger')).toBeInTheDocument()
    })

    it('shows the current number after the shop moved the order to another day', async () => {
      getPublic.mockResolvedValue(order({ number: 12 }))
      renderPage()
      expect(await screen.findByTestId('order-number')).toHaveTextContent('№ 12')
    })
  })
})
