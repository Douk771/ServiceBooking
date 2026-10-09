import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BookingPanel } from './BookingPanel'
import { calendarFixture, houseFixture, httpError, quoteFixture } from '../test/fixtures'
import type { CreateStayBookingInput } from '../types'

const api = vi.hoisted(() => ({
  calendar: vi.fn(),
  quote: vi.fn(),
  createBooking: vi.fn(),
}))
vi.mock('../api/publicStays', () => ({ publicStaysApi: api }))
// Cycle 40: the shared MessengerOptIn reads the lawyer's text through the shared client; a failure = the verbatim fallback.
vi.mock('@/api/client', () => ({ api: { get: () => Promise.reject(Object.assign(new Error('404'), { response: { status: 404 } })) } }))
vi.mock('../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

function Where() {
  return <span data-testid="where">{useLocation().pathname}</span>
}

function renderPanel(initial = {}, house = houseFixture()) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/lesnoy/dom-1']}>
        <Routes>
          <Route path="/lesnoy/dom-1" element={<BookingPanel house={house} initial={initial} onOpenTerms={() => undefined} />} />
          <Route path="/b/:token" element={<Where />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const cell = (day: number) => screen.findByRole('button', { name: new RegExp(`, ${day} января,`) })

async function fillGuest() {
  fireEvent.change(await screen.findByLabelText('Имя'), { target: { value: 'Анна' } })
  fireEvent.change(screen.getByLabelText('Телефон'), { target: { value: '9001234567' } })
}

beforeEach(() => {
  api.calendar.mockReset().mockResolvedValue(calendarFixture())
  api.quote.mockReset().mockResolvedValue(quoteFixture())
  api.createBooking.mockReset()
  sessionStorage.clear()
})

describe('BookingPanel', () => {
  it('shows the server quote for the chosen dates: lines, total, prepayment, due, tourist tax note', async () => {
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    expect(await screen.findByText('Проживание, 3 ночи')).toBeInTheDocument()
    expect(screen.getAllByText(/15\s000 ₽/).length).toBeGreaterThan(0)
    expect(screen.getByText('Предоплата 30 %')).toBeInTheDocument()
    expect(screen.getByText(/4\s500 ₽/)).toBeInTheDocument()
    expect(screen.getByText(/Цена проживания указана без туристического налога/)).toBeInTheDocument()
    expect(api.quote).toHaveBeenCalledWith('house-1', { checkIn: '2027-01-05', checkOut: '2027-01-08', adults: 2, children: 0, dogs: 0, needCot: false })
  })

  it('asks for dates first and does not quote without them', async () => {
    renderPanel()
    expect(await screen.findByText('Сначала выберите даты заезда и выезда')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Забронировать/ })).toBeDisabled()
    expect(api.quote).not.toHaveBeenCalled()
  })

  it('books against the total the guest saw and goes to the booking page; messenger stays off', async () => {
    api.createBooking.mockResolvedValue({ token: 'tok-1', bookingUrl: 'https://dom.ezbook.ru/b/tok-1', booking: {} })
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    await screen.findByText('Проживание, 3 ночи')
    await fillGuest()
    const button = screen.getByRole('button', { name: /Забронировать/ })
    await waitFor(() => expect(button).toBeEnabled())
    fireEvent.click(button)
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/b/tok-1'))
    const [houseId, body] = api.createBooking.mock.calls[0] as [string, CreateStayBookingInput]
    expect(houseId).toBe('house-1')
    expect(body).toMatchObject({ expectedTotalRub: 15000, guestName: 'Анна', guestPhone: '79001234567', notifyByMessenger: false, checkIn: '2027-01-05', checkOut: '2027-01-08' })
    expect(body.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/)
    // The booking is made: the next one on this house starts with a fresh key.
    expect(sessionStorage.getItem('dom:booking-key:house-1')).toBeNull()
  })

  it('QA CY37: a double click sends one request (one booking) while the first is in flight', async () => {
    let resolve!: (v: unknown) => void
    api.createBooking.mockReturnValue(new Promise((r) => { resolve = r }))
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    await screen.findByText('Проживание, 3 ночи')
    await fillGuest()
    const button = screen.getByRole('button', { name: /Забронировать/ })
    await waitFor(() => expect(button).toBeEnabled())
    fireEvent.click(button)
    fireEvent.click(button)
    fireEvent.click(button)
    await waitFor(() => expect(api.createBooking).toHaveBeenCalledTimes(1))
    fireEvent.click(button)
    expect(api.createBooking).toHaveBeenCalledTimes(1)
    resolve({ token: 'tok-2', bookingUrl: 'https://dom.ezbook.ru/b/tok-2', booking: {} })
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/b/tok-2'))
    expect(api.createBooking).toHaveBeenCalledTimes(1)
  })

  const offered = { offered: true, transports: ['Max' as const], checkboxLabel: 'Получать уведомления о брони в MAX' }

  it('cycle 40: no messenger box when the server does not offer it, and the request carries false', async () => {
    api.createBooking.mockResolvedValue({ token: 't', bookingUrl: 'u', booking: {} })
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    await screen.findByText('Проживание, 3 ночи')
    expect(screen.queryByTestId('messenger-opt-in')).toBeNull()
    await fillGuest()
    const button = screen.getByRole('button', { name: /Забронировать/ })
    await waitFor(() => expect(button).toBeEnabled())
    fireEvent.click(button)
    await waitFor(() => expect(api.createBooking).toHaveBeenCalled())
    expect((api.createBooking.mock.calls[0][1] as CreateStayBookingInput).notifyByMessenger).toBe(false)
  })

  it('cycle 40: the offered messenger consent is a separate, unchecked box with the server label that travels only when ticked', async () => {
    api.createBooking.mockResolvedValue({ token: 't', bookingUrl: 'u', booking: {} })
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' }, houseFixture({ messenger: offered }))
    await screen.findByText('Проживание, 3 ночи')
    const messenger = await screen.findByRole('checkbox', { name: 'Получать уведомления о брони в MAX' })
    expect(messenger).not.toBeChecked()
    expect(screen.getByText(/Текст согласия/)).toBeInTheDocument() // fallback §6.2
    fireEvent.click(messenger)
    await fillGuest()
    const button = screen.getByRole('button', { name: /Забронировать/ })
    await waitFor(() => expect(button).toBeEnabled())
    fireEvent.click(button)
    await waitFor(() => expect(api.createBooking).toHaveBeenCalled())
    expect((api.createBooking.mock.calls[0][1] as CreateStayBookingInput).notifyByMessenger).toBe(true)
  })

  it('a changed price is shown, the new quote replaces the old, and the retry reuses the SAME idempotency key (US-37-06)', async () => {
    const changed = quoteFixture({ totalRub: 16000, prepayRub: 4800, dueAtCheckInRub: 11200 })
    api.createBooking
      .mockRejectedValueOnce(httpError(409, { code: 'PriceChanged', message: 'Стоимость изменилась: 16 000 ₽. Проверьте и подтвердите бронь ещё раз', quote: changed }))
      .mockResolvedValueOnce({ token: 'tok-2', bookingUrl: 'u', booking: {} })
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    await screen.findByText('Проживание, 3 ночи')
    await fillGuest()
    fireEvent.click(await screen.findByRole('button', { name: /Забронировать/ }))
    const banner = await screen.findByTestId('price-changed')
    expect(banner).toHaveTextContent('Стоимость изменилась: 16 000 ₽')
    const confirm = screen.getByRole('button', { name: 'Подтвердить новую стоимость' })
    await waitFor(() => expect(confirm).toBeEnabled())
    fireEvent.click(confirm)
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/b/tok-2'))
    const [first, second] = api.createBooking.mock.calls.map((c) => c[1] as CreateStayBookingInput)
    expect(first.expectedTotalRub).toBe(15000)
    expect(second.expectedTotalRub).toBe(16000)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
  })

  it('dates taken meanwhile: the server text is shown and the dates start over', async () => {
    api.createBooking.mockRejectedValue(httpError(409, { code: 'DatesUnavailable', message: 'Эти даты уже заняты. Выберите другие' }))
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    await screen.findByText('Проживание, 3 ночи')
    await fillGuest()
    fireEvent.click(await screen.findByRole('button', { name: /Забронировать/ }))
    expect(await screen.findByText('Эти даты уже заняты. Выберите другие')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByTestId('stay-summary')).not.toBeInTheDocument())
  })

  it('a 400 text lands under its field', async () => {
    api.createBooking.mockRejectedValue(httpError(400, 'Введите номер телефона в формате +7 (900) 000-00-00'))
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    await screen.findByText('Проживание, 3 ночи')
    await fillGuest()
    fireEvent.click(await screen.findByRole('button', { name: /Забронировать/ }))
    expect(await screen.findByText('Введите номер телефона в формате +7 (900) 000-00-00')).toBeInTheDocument()
  })

  it('a 429 text is printed as it is', async () => {
    api.createBooking.mockRejectedValue(httpError(429, 'Слишком много неоплаченных броней. Оплатите или отмените текущую бронь'))
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    await screen.findByText('Проживание, 3 ночи')
    await fillGuest()
    fireEvent.click(await screen.findByRole('button', { name: /Забронировать/ }))
    expect(await screen.findByText('Слишком много неоплаченных броней. Оплатите или отмените текущую бронь')).toBeInTheDocument()
  })

  it('a house that does not take bookings shows the reason instead of the form', async () => {
    renderPanel({}, houseFixture({ acceptingBookings: false, notAcceptingText: 'Бронирование временно недоступно' }))
    expect(await screen.findByText('Бронирование временно недоступно')).toBeInTheDocument()
    expect(screen.queryByLabelText('Имя')).not.toBeInTheDocument()
  })

  it('the steppers stop at what the house takes (capacity, plus extra beds only when enabled)', async () => {
    renderPanel({ adults: 2 }, houseFixture({ capacity: 2, extraBeds: { enabled: false, max: 0, priceRub: 0 } }))
    expect(await screen.findByRole('button', { name: 'Взрослые: больше' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Дети: больше' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Взрослые: меньше' }))
    expect(screen.getByRole('button', { name: 'Взрослые: меньше' })).toBeDisabled() // at least one adult
  })

  it('with extra beds enabled the steppers go up to capacity + beds and the price note says so', async () => {
    renderPanel({ adults: 4 }, houseFixture({ capacity: 4, extraBeds: { enabled: true, max: 1, priceRub: 800 } }))
    const more = await screen.findByRole('button', { name: 'Взрослые: больше' })
    expect(more).toBeEnabled()
    fireEvent.click(more)
    expect(screen.getByRole('button', { name: 'Взрослые: больше' })).toBeDisabled()
    expect(screen.getByText(/Сверх вместимости \(4\) — 1 доп\. место, 800 ₽ за место в ночь/)).toBeInTheDocument()
  })

  it('a range from the URL that the calendar rejects is dropped with the reason', async () => {
    api.calendar.mockResolvedValue(calendarFixture({ '2027-01-06': 'Occupied' }))
    renderPanel({ checkIn: '2027-01-05', checkOut: '2027-01-08' })
    expect(await screen.findAllByText('Эти даты уже заняты. Выберите другие')).not.toHaveLength(0)
    expect(screen.queryByTestId('stay-summary')).not.toBeInTheDocument()
    expect(api.quote).not.toHaveBeenCalled()
  })

  it('picking dates on the calendar makes the quote', async () => {
    renderPanel()
    fireEvent.click(await cell(5))
    fireEvent.click(await cell(8))
    await waitFor(() => expect(api.quote).toHaveBeenCalledWith('house-1', expect.objectContaining({ checkIn: '2027-01-05', checkOut: '2027-01-08' })))
    expect(await screen.findByTestId('stay-summary')).toBeInTheDocument()
  })
})
