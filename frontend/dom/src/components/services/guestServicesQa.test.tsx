import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ServicePickDialog } from './ServicePickDialog'
import { ServiceQuoteSummary } from './ServiceQuoteSummary'
import type { ServiceQuoteDto, ServiceStartsDto } from '../../types'
import { httpError } from '../../test/fixtures'

vi.mock('../../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

// QA цикл 39 (SPEC US-39-08, US-39-09, ЮР39-6, ЮР39-8, Т39-04): гость ничего не выбирает «по умолчанию» и не видит служебных слов. Тесты написаны по SPEC, не по реализации.

const days = (id: string) => [
  { businessDate: '2027-01-15', label: 'пт 15 янв', hasStarts: true },
  { businessDate: '2027-01-16', label: 'сб 16 янв', hasStarts: false },
].map((d) => ({ ...d, label: `${d.label}${id ? '' : ''}` }))

const starts: ServiceStartsDto = {
  serviceId: 's1',
  businessDate: '2027-01-15',
  dateLabel: 'пт 15 янв',
  minHours: 2,
  maxHours: 3,
  starts: [{ startMinute: 1320, startUtc: '2027-01-15T15:00:00Z', label: '22:00', maxHours: 3, options: [{ hours: 2, endLabel: 'сб 16 янв, 00:00' }, { hours: 3, endLabel: 'сб 16 янв, 01:00' }] }],
}

const quote = (over: Partial<ServiceQuoteDto> = {}): ServiceQuoteDto => ({
  ok: true,
  problems: [],
  time: { businessDate: '2027-01-15', startMinute: 1320, endMinute: 1500, hours: 3, startUtc: '2027-01-15T15:00:00Z', endUtc: '2027-01-15T18:00:00Z', label: 'пт 15 янв, 22:00 — сб 16 янв, 01:00' },
  hourPrices: [],
  lines: [{ kind: 'Service', label: 'Баня · 3 ч', quantity: 1, unitPriceRub: 6000, amountRub: 6000 }],
  serviceAmountRub: 6000,
  itemsAmountRub: 0,
  totalRub: 6000,
  prepayPercent: 40,
  prepayRub: 2400,
  dueOnSiteRub: 3600,
  holdMinutes: 30,
  cancellationSummary: 'Расходы на подготовку: за 12 часов и раньше вся предоплата возвращается',
  payOnSiteText: null,
  acceptingBookings: true,
  notAcceptingText: null,
  ...over,
})

function renderDialog(over: Partial<React.ComponentProps<typeof ServicePickDialog>> = {}) {
  const loadStarts = vi.fn().mockResolvedValue(starts)
  const loadQuote = vi.fn().mockResolvedValue(quote())
  const onConfirm = vi.fn()
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ServicePickDialog
        title="Добавить услугу"
        services={[
          { id: 's1', name: 'Баня' },
          { id: 's2', name: 'Чан' },
        ]}
        staticDays={days}
        loadStarts={loadStarts}
        loadQuote={loadQuote}
        confirmLabel="Добавить"
        pending={false}
        error=""
        onConfirm={onConfirm}
        onClose={vi.fn()}
        {...over}
      />
    </QueryClientProvider>,
  )
  return { loadStarts, loadQuote, onConfirm }
}

describe('ServicePickDialog (QA цикл 39)', () => {
  it('with several services nothing is chosen, nothing is requested from the server and the button is disabled', () => {
    const { loadStarts, loadQuote } = renderDialog()
    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(2)
    radios.forEach((r) => expect(r).not.toBeChecked())
    expect(screen.getByText('Выберите услугу.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Добавить' })).toBeDisabled()
    expect(loadStarts).not.toHaveBeenCalled()
    expect(loadQuote).not.toHaveBeenCalled()
  })

  it('the button stays disabled until a service, a date, a start AND the hours are chosen; positions start at zero; only then the server quote is asked', async () => {
    const { loadQuote, onConfirm } = renderDialog()
    fireEvent.click(screen.getByRole('radio', { name: 'Баня' }))
    expect(screen.getByRole('button', { name: 'Добавить' })).toBeDisabled()
    expect(loadQuote).not.toHaveBeenCalled()
    fireEvent.click(await screen.findByRole('button', { name: /пт 15 янв/ }))
    expect(screen.getByRole('button', { name: 'Добавить' })).toBeDisabled()
    fireEvent.click(await screen.findByRole('button', { name: /Начало в 22:00/ }))
    expect(loadQuote).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Добавить' })).toBeDisabled()
    fireEvent.click(await screen.findByRole('button', { name: /^3 ч/ }))
    await waitFor(() => expect(loadQuote).toHaveBeenCalledWith('s1', { businessDate: '2027-01-15', startMinute: 1320, hours: 3, items: [] }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Добавить' })).toBeEnabled())
    fireEvent.click(screen.getByRole('button', { name: 'Добавить' }))
    expect(onConfirm).toHaveBeenCalledTimes(1)
    expect(onConfirm.mock.calls[0][0]).toBe('s1')
  })

  it('a quote the server refused (not ok / gate closed) can never be confirmed', async () => {
    renderDialog({ loadQuote: vi.fn().mockResolvedValue(quote({ ok: false, problems: [{ code: 'SlotTaken', message: 'Это время уже занято. Выберите другое' }], acceptingBookings: true })) })
    fireEvent.click(screen.getByRole('radio', { name: 'Баня' }))
    fireEvent.click(await screen.findByRole('button', { name: /пт 15 янв/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Начало в 22:00/ }))
    fireEvent.click(await screen.findByRole('button', { name: /^2 ч/ }))
    expect(await screen.findByTestId('service-quote-problems')).toHaveTextContent('Это время уже занято')
    expect(screen.getByRole('button', { name: 'Добавить' })).toBeDisabled()
  })

  it('every choice target of the guest is at least 44 px high (class contract), and a closed date says so in words', async () => {
    renderDialog()
    fireEvent.click(screen.getByRole('radio', { name: 'Баня' }))
    const closed = await screen.findByRole('button', { name: /сб 16 янв/ })
    expect(closed).toBeDisabled()
    expect(closed).toHaveTextContent('нет свободного времени')
    const label = screen.getByRole('radio', { name: 'Баня' }).closest('label')
    expect(label?.className).toContain('min-h-[44px]')
    for (const b of screen.getAllByRole('button', { name: /янв/ })) expect(b.className).toMatch(/min-h-\[(44|56)px\]/)
  })
})

describe('ServiceQuoteSummary (QA цикл 39)', () => {
  it('prints the server texts as they are and never invents «не меньше 0 ₽» or «бизнес-день»', () => {
    render(<ServiceQuoteSummary quote={quote()} />)
    const text = document.body.textContent ?? ''
    expect(text).toContain('пт 15 янв, 22:00 — сб 16 янв, 01:00')
    expect(text).toContain('Итого')
    expect(text).not.toMatch(/не меньше 0/)
    expect(text.toLowerCase()).not.toContain('бизнес')
    expect(text).not.toMatch(/задаток|невозвратн|депозит/i)
  })

  it('a free (0 ₽) position is printed as «бесплатно», pay-on-site text replaces the prepayment block when there is none', () => {
    render(
      <ServiceQuoteSummary
        quote={quote({ prepayRub: 0, prepayPercent: null, holdMinutes: null, payOnSiteText: 'Оплата на месте, в компании. Через сервис оплата не производится', lines: [
          { kind: 'Service', label: 'Баня · 3 ч', quantity: 1, unitPriceRub: 6000, amountRub: 6000 },
          { kind: 'Item', label: 'Полотенце × 2', quantity: 2, unitPriceRub: 0, amountRub: 0 },
        ] })}
      />,
    )
    expect(screen.getByText('бесплатно')).toBeInTheDocument()
    expect(screen.getByText(/Оплата на месте, в компании/)).toBeInTheDocument()
    expect(screen.queryByText(/Предоплата/)).not.toBeInTheDocument()
  })
})
