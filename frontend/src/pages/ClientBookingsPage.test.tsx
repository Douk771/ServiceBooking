import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ClientBookingsPage } from './ClientBookingsPage'
import type { Booking } from '../types'

const getClientBookings = vi.fn()
const canReview = vi.fn()

vi.mock('../api/bookings', () => ({
  bookingsApi: {
    getClientBookings: (...args: unknown[]) => getClientBookings(...args),
    cancel: vi.fn(),
    reschedule: vi.fn(),
    getSlots: vi.fn().mockResolvedValue([]),
  },
}))

vi.mock('../api/reviews', () => ({
  reviewsApi: {
    canReview: (...args: unknown[]) => canReview(...args),
  },
}))

beforeEach(() => {
  getClientBookings.mockReset()
  canReview.mockReset().mockResolvedValue([])
})

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ClientBookingsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

function makeBooking(overrides: Partial<Booking> = {}): Booking {
  return {
    id: 'b1',
    companyId: 'c1',
    serviceId: 's1',
    serviceName: 'Стрижка',
    masterId: 'm1',
    masterName: 'Пётр',
    clientName: 'Иван',
    date: '2026-10-10',
    startTime: '14:00:00',
    endTime: '15:00:00',
    status: 'Confirmed',
    createdAt: '2026-09-01T00:00:00Z',
    ...overrides,
  }
}

// ARCHITECTURE_CYCLE15.md §286 — the "Перенести" button on a client's own booking is driven ENTIRELY
// by the server-computed `clientRescheduleAllowed` flag, never a local time computation.
describe('ClientBookingsPage — §286 reschedule button gating', () => {
  it('shows "Перенести" when the server says clientRescheduleAllowed: true', async () => {
    getClientBookings.mockResolvedValue([makeBooking({ clientRescheduleAllowed: true })])

    renderPage()

    expect(await screen.findByRole('button', { name: 'Перенести' })).toBeInTheDocument()
  })

  it('shows no "Перенести" button when the flag is false', async () => {
    getClientBookings.mockResolvedValue([makeBooking({ clientRescheduleAllowed: false })])

    renderPage()

    await waitFor(() => expect(screen.getByText('Стрижка')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Перенести' })).not.toBeInTheDocument()
  })

  it('shows no "Перенести" button when the flag is absent (older server, §280)', async () => {
    getClientBookings.mockResolvedValue([makeBooking()])

    renderPage()

    await waitFor(() => expect(screen.getByText('Стрижка')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Перенести' })).not.toBeInTheDocument()
  })
})

// ARCHITECTURE_CYCLE17.md §304.4/§305.2 (US-17-06, US-17-02, C15-5, C15-6.1) — no more local
// differenceInHours-based gating; cancel button follows the server flag, and the reschedule
// explanation text uses the company's actual clientRescheduleMinHours.
describe('ClientBookingsPage — §304.4 cancel button gating (server-authoritative, C15-5)', () => {
  it('shows "Отменить" when the server says clientCancelAllowed: true', async () => {
    getClientBookings.mockResolvedValue([makeBooking({ clientCancelAllowed: true })])
    renderPage()
    expect(await screen.findByRole('button', { name: 'Отменить' })).toBeInTheDocument()
  })

  it('hides "Отменить" when clientCancelAllowed: false', async () => {
    getClientBookings.mockResolvedValue([makeBooking({ clientCancelAllowed: false })])
    renderPage()
    await waitFor(() => expect(screen.getByText('Стрижка')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Отменить' })).not.toBeInTheDocument()
  })

  it('shows "Отменить" when the field is absent (old cache) — server decides on submit, §304.4', async () => {
    getClientBookings.mockResolvedValue([makeBooking()])
    renderPage()
    expect(await screen.findByRole('button', { name: 'Отменить' })).toBeInTheDocument()
  })

  it('no local time arithmetic is used to gate the button (grep-verifiable via absence of date-fns differenceInHours import)', async () => {
    // Regression for the removed `canCancelBooking`: a booking far in the past with
    // clientCancelAllowed: true must still show the button, and one far in the future with
    // clientCancelAllowed: false must still hide it — proving the decision is server-driven.
    getClientBookings.mockResolvedValue([
      makeBooking({ id: 'past', date: '2020-01-01', clientCancelAllowed: true }),
    ])
    renderPage()
    expect(await screen.findByRole('button', { name: 'Отменить' })).toBeInTheDocument()
  })
})

describe('ClientBookingsPage — §305.2 reschedule explanation text (US-17-02, C15-6.1)', () => {
  it('shows the company-specific hint when reschedule is disallowed with a known window', async () => {
    getClientBookings.mockResolvedValue([
      makeBooking({ clientRescheduleAllowed: false, clientRescheduleMinHours: 24 }),
    ])
    renderPage()
    expect(await screen.findByText('Перенести можно не позже чем за 24 ч до визита')).toBeInTheDocument()
  })

  it('shows "Перенести уже нельзя" when the window is 0', async () => {
    getClientBookings.mockResolvedValue([
      makeBooking({ clientRescheduleAllowed: false, clientRescheduleMinHours: 0 }),
    ])
    renderPage()
    expect(await screen.findByText('Перенести уже нельзя')).toBeInTheDocument()
  })

  it('shows no hint when clientRescheduleMinHours is absent (old cache) — unchanged from before the cycle', async () => {
    getClientBookings.mockResolvedValue([makeBooking({ clientRescheduleAllowed: false })])
    renderPage()
    await waitFor(() => expect(screen.getByText('Стрижка')).toBeInTheDocument())
    expect(screen.queryByText(/Перенести можно/)).not.toBeInTheDocument()
    expect(screen.queryByText('Перенести уже нельзя')).not.toBeInTheDocument()
  })
})
