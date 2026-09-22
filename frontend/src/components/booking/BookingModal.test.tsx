import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { format } from 'date-fns'
import { BookingModal } from './BookingModal'
import { DAY_FULL_LABEL } from './BookingCalendar'
import type { Company, Service } from '../../types'
import type { MasterPublicDto } from '../../api/companies'
import type { AvailabilityResponse } from '../../api/bookings'

const getMasters = vi.fn()
const getAvailability = vi.fn()
const getSlots = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: {
    getMasters: (...args: unknown[]) => getMasters(...args),
  },
}))

vi.mock('../../api/bookings', async () => {
  const actual = await vi.importActual<typeof import('../../api/bookings')>('../../api/bookings')
  return {
    ...actual,
    bookingsApi: {
      getAvailability: (...args: unknown[]) => getAvailability(...args),
      getSlots: (...args: unknown[]) => getSlots(...args),
      create: vi.fn(),
    },
  }
})

const company: Company = {
  id: 'co1',
  name: 'Барбершоп «Гвоздь»',
  slug: 'gvozd',
  allowSelfBooking: true,
}

const service: Service = {
  id: 'svc1',
  companyId: 'co1',
  name: 'Стрижка',
  durationMinutes: 30,
  price: 1500,
}

function master(overrides: Partial<MasterPublicDto> = {}): MasterPublicDto {
  return { userId: 'm1', firstName: 'Иван', lastName: 'Петров', ...overrides }
}

function monthKey(offsetMonths = 0) {
  const d = new Date()
  d.setDate(1)
  d.setMonth(d.getMonth() + offsetMonths)
  return format(d, 'yyyy-MM')
}

// A date guaranteed to (a) be in the future and (b) fall within the currently displayed month —
// avoids the test accidentally picking a day that's already past "today" in whatever environment
// runs it, which the calendar always renders as disabled regardless of server status.
function futureDateInCurrentMonth(): string {
  const today = new Date()
  const lastDayOfMonth = new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate()
  const day = Math.min(today.getDate() + 3, lastDayOfMonth)
  return format(new Date(today.getFullYear(), today.getMonth(), day), 'yyyy-MM-dd')
}

function availabilityFor(dates: Record<string, AvailabilityResponse['days'][number]['status']>): AvailabilityResponse {
  const days = Object.entries(dates).map(([date, status]) => ({
    date,
    status,
    lastFreeSlotStart: status === 'Available' ? '18:00:00' : null,
  }))
  return {
    from: `${monthKey()}-01`,
    to: `${monthKey()}-28`,
    totalDurationMinutes: 30,
    stepMinutes: 30,
    horizonDays: 90,
    horizonLastDate: '2099-01-01',
    days,
  }
}

function renderModal() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BookingModal service={service} company={company} onClose={() => {}} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getMasters.mockReset()
  getAvailability.mockReset().mockResolvedValue(availabilityFor({}))
  getSlots.mockReset().mockResolvedValue([])
})

describe('BookingModal — US-64 single/zero master', () => {
  it('skips the master step and shows a 3-step progress bar when exactly one master provides the service', async () => {
    getMasters.mockResolvedValueOnce([master()])
    renderModal()

    // The master-choosing step never renders...
    await waitFor(() => expect(screen.queryByText('Выберите мастера')).not.toBeInTheDocument())
    // ...and the date step is shown right away instead.
    expect(await screen.findByText('Выберите дату')).toBeInTheDocument()

    // Progress bar renders exactly 3 segments (date, slot, info) — not the usual 4.
    const segments = document.querySelectorAll('.flex.gap-1\\.5.mt-4 > div')
    expect(segments.length).toBe(3)
  })

  it("shows the sole master's name in the confirmation summary — a client must not book \"into the void\"", async () => {
    getMasters.mockResolvedValueOnce([master({ firstName: 'Анна', lastName: 'Смирнова' })])
    // A future (not "today") date, so the modal's same-day "already passed" slot filter can't
    // interfere with this test regardless of what time of day the suite happens to run.
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '10:30:00' }])

    renderModal()
    await screen.findByText('Выберите дату')

    const dateLabel = format(new Date(`${dateStr}T00:00:00`), 'd MMMM', { locale: (await import('date-fns/locale')).ru })
    const dayButton = (await screen.findByLabelText(dateLabel)) as HTMLButtonElement
    dayButton.click()

    const slotButton = await screen.findByRole('button', { name: '10:00' })
    slotButton.click()

    expect(await screen.findByText('Анна Смирнова')).toBeInTheDocument()
  })

  it('shows an understandable "cannot book now" message when zero active masters provide the service', async () => {
    getMasters.mockResolvedValueOnce([])
    renderModal()

    expect(await screen.findByText('Сейчас записаться нельзя')).toBeInTheDocument()
    expect(screen.queryByText('Выберите дату')).not.toBeInTheDocument()
  })
})

describe('BookingModal — US-65 calendar day states', () => {
  it('does not allow clicking a day off', async () => {
    getMasters.mockResolvedValueOnce([master(), master({ userId: 'm2' })])
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'DayOff' }))
    renderModal()

    const masterButtons = await screen.findAllByRole('button', { name: /Иван Петров/i })
    masterButtons[0].click()

    await waitFor(() => expect(screen.getByText('Выберите дату')).toBeInTheDocument())
    const dayOffCell = await screen.findByText('выходной')
    const button = dayOffCell.closest('button') as HTMLButtonElement
    expect(button).toBeDisabled()
    expect(button.getAttribute('aria-disabled')).toBe('true')
  })

  it('marks a fully booked working day as not clickable and labels it', async () => {
    getMasters.mockResolvedValueOnce([master(), master({ userId: 'm2' })])
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'FullyBooked' }))
    renderModal()

    const masterButtons = await screen.findAllByRole('button', { name: /Иван Петров/i })
    masterButtons[0].click()

    await waitFor(() => expect(screen.getByText('Выберите дату')).toBeInTheDocument())
    // The legend repeats the same DAY_FULL_LABEL text, so match on the calendar day button's
    // accessible name (which embeds the label) rather than on text content alone.
    const button = await screen.findByRole('button', { name: new RegExp(DAY_FULL_LABEL) })
    expect(button).toBeDisabled()
    expect(button.getAttribute('aria-disabled')).toBe('true')
  })
})
