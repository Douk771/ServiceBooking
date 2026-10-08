import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, within, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { BookingSessionsBlock } from './BookingSessionsBlock'
import { bookingFixture, httpError } from '../../test/fixtures'
import type { PublicBookingSessionDto, PublicStayBookingWithServices } from '../../types'

const api = vi.hoisted(() => ({ services: vi.fn(), serviceStarts: vi.fn(), serviceQuote: vi.fn(), addSession: vi.fn(), cancelSession: vi.fn() }))
vi.mock('../../api/guestBookings', () => ({ guestBookingsApi: api }))
vi.mock('../../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

const session = (over: Partial<PublicBookingSessionDto> = {}): PublicBookingSessionDto => ({
  id: 's1',
  serviceName: 'Баня',
  time: { businessDate: '2027-01-15', startMinute: 1320, endMinute: 1500, hours: 3, startUtc: '2027-01-15T15:00:00Z', endUtc: '2027-01-15T18:00:00Z', label: 'пт 15 янв, 22:00 — сб 16 янв, 01:00' },
  items: [],
  totalRub: 6000,
  state: 'Active',
  stateText: 'Забронировано',
  canCancel: true,
  addedByStaff: false,
  ...over,
})

const booking = (over: Partial<PublicStayBookingWithServices> = {}): PublicStayBookingWithServices =>
  ({ ...bookingFixture(), sessions: [], servicesBlock: { canAdd: true }, ...over }) as PublicStayBookingWithServices

function renderBlock(b: PublicStayBookingWithServices, onBooking = vi.fn()) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <BookingSessionsBlock token="tok" booking={b} onBooking={onBooking} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return onBooking
}

beforeEach(() => Object.values(api).forEach((f) => f.mockReset()))

describe('BookingSessionsBlock', () => {
  it('shows nothing when there are no sessions and none can be added', () => {
    renderBlock(booking({ servicesBlock: { canAdd: false, cannotAddText: 'Бронь завершена' } }))
    expect(screen.queryByTestId('booking-sessions')).not.toBeInTheDocument()
  })

  it('offers «Добавить услугу» with nothing chosen and says services are paid on the spot', () => {
    renderBlock(booking({ servicesBlock: { canAdd: true, hint: 'Сеанс сохранится, если бронь будет оплачена' } }))
    expect(screen.getByRole('button', { name: /Добавить услугу/ })).toBeEnabled()
    expect(screen.getByText('Услуг пока нет. Баню или чан можно добавить к брони на время проживания.')).toBeInTheDocument()
    expect(screen.getByText(/оплачиваются на месте и в предоплату не входят/)).toBeInTheDocument()
    expect(screen.getByText('Сеанс сохранится, если бронь будет оплачена')).toBeInTheDocument()
  })

  it('a session added by the staff sits in its own box «Добавлено по вашей просьбе» with the way to cancel it (ЮР39-6)', () => {
    renderBlock(booking({ sessions: [session({ addedByStaff: true, addedByStaffText: 'Добавил(а) сотрудник компании по телефону' })] }))
    const box = screen.getByTestId('session-added-by-staff')
    expect(box).toHaveTextContent('Добавлено по вашей просьбе')
    expect(box).toHaveTextContent('Если вы этого не просили, отмените — без последствий')
    expect(screen.getByTestId('booking-session')).toHaveTextContent('пт 15 янв, 22:00 — сб 16 янв, 01:00')
  })

  it('explains why a started session cannot be cancelled instead of showing a dead button', () => {
    renderBlock(booking({ sessions: [session({ canCancel: false, cannotCancelText: 'Сеанс уже начался — по вопросам свяжитесь с компанией: +7 (900) 111-22-33' })] }))
    expect(screen.queryByRole('button', { name: 'Отменить сеанс' })).not.toBeInTheDocument()
    expect(screen.getByText(/Сеанс уже начался/)).toBeInTheDocument()
  })

  it('cancelling asks first, says nothing was paid, and puts the answer of the server on the page', async () => {
    const after = booking({ sessions: [session({ state: 'CancelledByGuest', stateText: 'Отменён вами', canCancel: false })] })
    api.cancelSession.mockResolvedValue(after)
    const onBooking = renderBlock(booking({ sessions: [session()] }))
    fireEvent.click(screen.getByRole('button', { name: 'Отменить сеанс' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByTestId('refund-text')).toHaveTextContent('Оплата за сеанс не вносилась — отмена без последствий.')
    expect(within(dialog).queryByText(/Возврат делает компания/)).not.toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Отменить сеанс' }))
    await waitFor(() => expect(api.cancelSession).toHaveBeenCalledWith('tok', 's1'))
    await waitFor(() => expect(onBooking).toHaveBeenCalledWith(after))
  })
})
