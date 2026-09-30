import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ClientBookingsPage } from './ClientBookingsPage'
import { SHOWCASE_FALLBACK_TEXTS } from '../utils/showcaseTexts'
import type { Booking } from '../types'

// Cycle 28 (API_CONTRACT_CYCLE28.md §593): a visit in a showcase company says so on its own card.

const getClientBookings = vi.fn()
vi.mock('../api/bookings', () => ({
  bookingsApi: { getClientBookings: (...a: unknown[]) => getClientBookings(...a), cancel: vi.fn(), reschedule: vi.fn(), getSlots: vi.fn() },
}))
vi.mock('../api/reviews', () => ({ reviewsApi: { canReview: vi.fn().mockResolvedValue([]) } }))
vi.mock('../api/legal', () => ({ legalApi: { getText: vi.fn().mockRejectedValue({ response: { status: 404 } }) } }))

function booking(over: Partial<Booking>): Booking {
  return {
    id: 'b1',
    companyId: 'c1',
    companyName: 'Салон «Пример»',
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
    ...over,
  }
}

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

beforeEach(() => getClientBookings.mockReset())

describe('ClientBookingsPage — showcase visits', () => {
  it('a showcase visit carries the notice; an ordinary one next to it does not', async () => {
    getClientBookings.mockResolvedValue([
      booking({ id: 'b1', companyIsShowcase: true }),
      booking({ id: 'b2', companyName: 'Барбершоп', serviceName: 'Борода', companyIsShowcase: false }),
    ])
    renderPage()

    await screen.findByText('Борода')
    const notices = screen.getAllByTestId('showcase-notice')
    expect(notices).toHaveLength(1)
    expect(notices[0]).toHaveTextContent(SHOWCASE_FALLBACK_TEXTS.ShowcaseNotice)
    expect(screen.getByText('Стрижка').closest('div.bg-white')).toContainElement(notices[0])
  })

  it('an older server without the field shows no notice', async () => {
    getClientBookings.mockResolvedValue([booking({})])
    renderPage()

    await screen.findByText('Стрижка')
    expect(screen.queryByTestId('showcase-notice')).not.toBeInTheDocument()
  })
})
