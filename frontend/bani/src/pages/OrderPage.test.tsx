import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import { OrderPage } from './OrderPage'
import { renderInVertical, testVertical } from '../test/baniTestVertical'
import type { PublicServiceOrderDto } from '@/types/slots'

const orders = vi.hoisted(() => ({ get: vi.fn(), cancel: vi.fn(), uploadProof: vi.fn(), proofBlob: vi.fn(), pushSubscribe: vi.fn(), pushUnsubscribe: vi.fn() }))

const order = (over: Partial<PublicServiceOrderDto> = {}): PublicServiceOrderDto =>
  ({
    status: 'Confirmed',
    displayStatus: 'Confirmed',
    statusText: 'Подтверждена',
    serverTimeUtc: '2027-01-14T10:00:00Z',
    service: { name: 'Баня по-чёрному', url: '/sherbany/banya', coverUrl: null },
    company: { name: 'Щербаны', phone: null, url: '/sherbany', address: null, yandexMapsUrl: null, twoGisUrl: null },
    provider: { status: 'IndividualEntrepreneur', statusLabel: 'Индивидуальный предприниматель', name: 'ИП Иванов', inn: '123456789012', ogrn: null, claimsAddress: 'Шерегеш' },
    time: { businessDate: '2027-01-15', startMinute: 1320, endMinute: 1500, hours: 3, startUtc: '', endUtc: '', label: 'пт 15 янв, 22:00 — сб 16 янв, 01:00' },
    items: [],
    lines: [],
    hourPrices: [],
    serviceAmountRub: 6000,
    itemsAmountRub: 0,
    totalRub: 6000,
    prepayPercent: 0,
    prepayRub: 0,
    dueOnSiteRub: 6000,
    payment: null,
    paymentProofs: [],
    proofs: { canAttach: false, maxCount: 3, maxBytes: 1, acceptedTypes: [] },
    cancellation: { policy: 'PreparationCosts', summary: 'Отмена за 12 ч', canCancel: true, refund: { kind: 'NothingPaid', refundAtLeastRub: null, maxDeductionRub: null, text: '' } },
    guestName: 'Анна',
    guestPhoneMasked: null,
    guestsCount: 4,
    notifications: { webPush: { available: false }, messengerSelected: false },
    availableActions: ['Cancel'],
    ...over,
  }) as unknown as PublicServiceOrderDto

function show(o: PublicServiceOrderDto | Error) {
  if (o instanceof Error) orders.get.mockRejectedValue(o)
  else orders.get.mockResolvedValue(o)
  renderInVertical(testVertical({ orders }), '/s/tok', '/s/:token', <OrderPage />)
}

beforeEach(() => Object.values(orders).forEach((f) => f.mockReset()))

describe('OrderPage (bani)', () => {
  it('shows the reminder block when the reminder was sent, the guests count and the local time note', async () => {
    show(order({ localTimeNote: 'Время местное, Шерегеш', sessionReminder: { sentAtUtc: '2027-01-15T12:00:00Z', text: 'Сеанс сегодня в 22:00.' } }))
    expect(await screen.findByTestId('session-reminder')).toHaveTextContent('Напоминание')
    expect(screen.getByTestId('session-reminder')).toHaveTextContent('Сеанс сегодня в 22:00.')
    expect(screen.getByTestId('local-time-note')).toHaveTextContent('Время местное, Шерегеш')
    expect(screen.getByText('Гостей')).toBeInTheDocument()
  })

  it('has no reminder block until the server sets one', async () => {
    show(order({ sessionReminder: null }))
    await screen.findByTestId('order-status')
    expect(screen.queryByTestId('session-reminder')).toBeNull()
  })

  it('«Забронировать ещё» leads to the bare company address — nothing personal in the URL (Т42-09)', async () => {
    show(order({ bookAgainUrl: '/sherbany' }))
    const link = await screen.findByTestId('book-again')
    expect(link).toHaveAttribute('href', '/sherbany')
    expect(link.getAttribute('href')).not.toMatch(/[?&=]/)
  })

  it('has no «Забронировать ещё» without bookAgainUrl', async () => {
    show(order({ bookAgainUrl: null }))
    await screen.findByTestId('order-status')
    expect(screen.queryByTestId('book-again')).toBeNull()
  })

  it('speaks of «бронь», not «заказ», on the page', async () => {
    show(order())
    await screen.findByTestId('order-status')
    const text = (document.body.textContent ?? '').toLowerCase()
    expect(text).toContain('ваша бронь')
    expect(text).not.toContain('заказ')
  })

  it('unknown link: «Бронь не найдена»', async () => {
    show(Object.assign(new Error('nf'), { response: { status: 404, data: '' } }))
    expect(await screen.findByText('Бронь не найдена')).toBeInTheDocument()
  })

  it('a load failure is shown with a retry, in the word «бронь»', async () => {
    show(Object.assign(new Error('boom'), { response: { status: 500, data: '' } }))
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
