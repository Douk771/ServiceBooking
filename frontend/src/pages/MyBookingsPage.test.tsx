import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { MyBookingsPage } from './MyBookingsPage'

vi.mock('../api/bookings', () => ({
  bookingsApi: { getMasterBookings: vi.fn().mockResolvedValue([]), getSlots: vi.fn().mockResolvedValue([]) },
}))
vi.mock('../api/masters', () => ({ mastersApi: { getClients: vi.fn().mockResolvedValue([]) } }))
vi.mock('../api/clientNotes', () => ({ clientNotesApi: {} }))
vi.mock('../components/booking/BookingModal', () => ({ BookingModal: () => null }))
vi.mock('../components/booking/RescheduleModal', () => ({ RescheduleModal: () => null }))

describe('MyBookingsPage — cycle 33 (§33.10.2)', () => {
  it('replaces the device card with a link to /profile#devices', async () => {
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MemoryRouter>
          <MyBookingsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    const link = await screen.findByRole('link', { name: 'Уведомления на устройства — в профиле' })
    expect(link).toHaveAttribute('href', '/profile#devices')
  })
})
