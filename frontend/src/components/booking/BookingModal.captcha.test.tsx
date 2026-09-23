import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { format } from 'date-fns'
import { BookingModal } from './BookingModal'
import { useAuthStore } from '../../store/authStore'
import type { Company, Service } from '../../types'
import type { MasterPublicDto } from '../../api/companies'
import type { AvailabilityResponse } from '../../api/bookings'

// Review finding §5 — `BookingModal.test.tsx` mocks `smartCaptchaEnabled: false` at the module
// level, so the "guest sees SmartCaptcha and can't submit without a token" branch (§108.6 lists
// captcha among what a merge can silently break) was never exercised anywhere. This file is a
// separate module specifically so it can mock the opposite value without touching the other suite.
const getMy = vi.fn()
const getMemberOf = vi.fn()
const getMasters = vi.fn()
const getAvailability = vi.fn()
const getSlots = vi.fn()
const createBooking = vi.fn()
const getByCompany = vi.fn()
const getText = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: {
    getMy: (...args: unknown[]) => getMy(...args),
    getMemberOf: (...args: unknown[]) => getMemberOf(...args),
    getMasters: (...args: unknown[]) => getMasters(...args),
  },
}))

vi.mock('../../api/services', () => ({
  servicesApi: {
    getByCompany: (...args: unknown[]) => getByCompany(...args),
  },
}))

vi.mock('../../api/bookings', async () => {
  const actual = await vi.importActual<typeof import('../../api/bookings')>('../../api/bookings')
  return {
    ...actual,
    bookingsApi: {
      getAvailability: (...args: unknown[]) => getAvailability(...args),
      getSlots: (...args: unknown[]) => getSlots(...args),
      create: (...args: unknown[]) => createBooking(...args),
    },
  }
})

vi.mock('../../api/legal', () => ({
  legalApi: { getText: (...args: unknown[]) => getText(...args) },
}))

// The real widget needs a live script + site key; a fake stand-in button lets the test drive
// "user solved the captcha" without any of that, while still exercising the modal's own gating
// logic (disabled submit until `onToken` fires, token sent as `captchaToken`).
vi.mock('./SmartCaptcha', () => ({
  smartCaptchaEnabled: true,
  SmartCaptcha: ({ onToken }: { onToken: (token: string) => void }) => (
    <button type="button" onClick={() => onToken('fake-captcha-token')}>
      Solve captcha
    </button>
  ),
}))

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
  return { userId: 'm1', firstName: 'Иван', lastName: 'Петров', providesServices: true, ...overrides }
}

const FIXED_NOW = new Date('2026-09-15T09:00:00')

function futureDateInCurrentMonth(): string {
  const today = new Date()
  const lastDayOfMonth = new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate()
  const day = Math.min(today.getDate() + 3, lastDayOfMonth)
  return format(new Date(today.getFullYear(), today.getMonth(), day), 'yyyy-MM-dd')
}

function availabilityFor(
  dates: Record<string, AvailabilityResponse['days'][number]['status']>,
): AvailabilityResponse {
  const days = Object.entries(dates).map(([date, status]) => ({
    date,
    status,
    lastFreeSlotStart: status === 'Available' ? '18:00:00' : null,
    scheduleState: null,
  }))
  return {
    from: days[0]?.date ?? '2026-09-01',
    to: days[0]?.date ?? '2026-09-28',
    totalDurationMinutes: 30,
    stepMinutes: 30,
    horizonDays: 90,
    horizonLastDate: '2099-01-01',
    staffMode: false,
    days,
  }
}

function renderModal() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BookingModal service={service} company={company} onClose={() => {}} allowMultipleServices={false} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

async function reachInfoStep(user: ReturnType<typeof userEvent.setup>, dateStr: string) {
  const dateLabel = format(new Date(`${dateStr}T00:00:00`), 'd MMMM', { locale: (await import('date-fns/locale')).ru })
  await user.click(await screen.findByLabelText(dateLabel))
  await user.click(await screen.findByRole('button', { name: '10:00' }))
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['Date'] })
  vi.setSystemTime(FIXED_NOW)

  getMy.mockReset().mockResolvedValue([])
  getMemberOf.mockReset().mockResolvedValue([])
  getMasters.mockReset().mockResolvedValue([master()])
  getAvailability.mockReset().mockResolvedValue(availabilityFor({}))
  getSlots.mockReset().mockResolvedValue([])
  createBooking.mockReset().mockResolvedValue({})
  getByCompany.mockReset().mockResolvedValue([])
  getText.mockReset().mockRejectedValue(new Error('not needed for this suite'))
  useAuthStore.setState({ user: null, token: null })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('BookingModal — guest SmartCaptcha gating (§108.6)', () => {
  it('shows the SmartCaptcha widget for an unauthenticated guest and disables submit until it yields a token', async () => {
    const user = userEvent.setup()
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '10:30:00' }])
    renderModal()

    await screen.findByText('Выберите дату')
    await reachInfoStep(user, dateStr)

    expect(screen.getByText('Solve captcha')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')

    const submit = screen.getByRole('button', { name: 'Подтвердить запись' })
    expect(submit).toBeDisabled()

    await user.click(submit)
    expect(createBooking).not.toHaveBeenCalled()
  })

  it('sends the captchaToken once the widget yields one, and lets the guest submit', async () => {
    const user = userEvent.setup()
    createBooking.mockResolvedValueOnce({ id: 'b1' })
    const dateStr = futureDateInCurrentMonth()
    getAvailability.mockResolvedValue(availabilityFor({ [dateStr]: 'Available' }))
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '10:30:00' }])
    renderModal()

    await screen.findByText('Выберите дату')
    await reachInfoStep(user, dateStr)

    await user.type(screen.getByLabelText('Ваше имя *'), 'Иван Иванов')
    await user.type(screen.getByLabelText('Телефон *'), '+79991234567')
    await user.click(screen.getByText('Solve captcha'))

    const submit = screen.getByRole('button', { name: 'Подтвердить запись' })
    expect(submit).not.toBeDisabled()
    await user.click(submit)

    await waitFor(() => expect(createBooking).toHaveBeenCalled())
    const payload = createBooking.mock.calls[0][0]
    expect(payload.captchaToken).toBe('fake-captcha-token')
  })
})
