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
