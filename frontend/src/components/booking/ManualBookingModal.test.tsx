import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { Company, Service } from '../../types'
import type { MasterPublicDto } from '../../api/companies'

const getMy = vi.fn()
const getMemberOf = vi.fn()
const getMasters = vi.fn()
const getByCompany = vi.fn()
const getSlots = vi.fn()
const createBooking = vi.fn()

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
      ...actual.bookingsApi,
      getSlots: (...args: unknown[]) => getSlots(...args),
      create: (...args: unknown[]) => createBooking(...args),
    },
  }
})

// Imported after the mocks above so the module under test picks them up.
const { ManualBookingModal } = await import('./ManualBookingModal')

const company: Company = { id: 'co1', name: 'Барбершоп «Гвоздь»', slug: 'gvozd', allowSelfBooking: true }
const service: Service = { id: 'svc1', companyId: 'co1', name: 'Стрижка', durationMinutes: 30, price: 1500 }
const secondService: Service = { id: 'svc2', companyId: 'co1', name: 'Окрашивание', durationMinutes: 60, price: 2500 }

function master(overrides: Partial<MasterPublicDto> = {}): MasterPublicDto {
  return { userId: 'm1', firstName: 'Иван', lastName: 'Петров', ...overrides }
}

function renderModal() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <ManualBookingModal onClose={() => {}} />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getMy.mockReset().mockResolvedValue([company])
  getMemberOf.mockReset().mockResolvedValue([])
  getMasters.mockReset()
  getByCompany.mockReset().mockResolvedValue([service, secondService])
  getSlots.mockReset().mockResolvedValue([])
  createBooking.mockReset().mockResolvedValue({})
})

describe('ManualBookingModal — US-64 single master, applied the same way as client booking', () => {
  it('skips the master step when exactly one master provides the (first) selected service', async () => {
    getMasters.mockResolvedValue([master()])
    renderModal()

    // Single company auto-selected → straight to the service picker.
    await screen.findByText('Выберите услуги')
    ;(await screen.findByText('Стрижка')).click()
    ;(await screen.findByText('Продолжить')).click()

    // The master-choosing step must never render...
    expect(screen.queryByText('Выберите мастера')).not.toBeInTheDocument()
    // ...and the flow lands directly on the date/time step instead.
    expect(await screen.findByText('Дата')).toBeInTheDocument()
  })

  it('still shows the master step when there is a real choice', async () => {
    getMasters.mockResolvedValue([master(), master({ userId: 'm2', firstName: 'Анна', lastName: 'Смирнова' })])
    renderModal()

    await screen.findByText('Выберите услуги')
    ;(await screen.findByText('Стрижка')).click()
    ;(await screen.findByText('Продолжить')).click()

    expect(await screen.findByText('Выберите мастера')).toBeInTheDocument()
  })
})

describe('ManualBookingModal — US-67 multiple services per visit', () => {
  it('sums duration and price once staff add a second service, same as the client flow', async () => {
    getMasters.mockResolvedValue([master()])
    renderModal()

    await screen.findByText('Выберите услуги')
    ;(await screen.findByText('Стрижка')).click()
    ;(await screen.findByText('Окрашивание')).click()

    expect(await screen.findByText('90 мин · 4 000 ₽')).toBeInTheDocument()
  })

  it('refuses more than 5 services with an explanatory message', async () => {
    const extraServices = Array.from({ length: 4 }, (_, i) => ({
      id: `extra-${i}`,
      companyId: 'co1',
      name: `Доп. услуга ${i}`,
      durationMinutes: 10,
      price: 100,
    }))
    getByCompany.mockResolvedValue([service, secondService, ...extraServices])
    getMasters.mockResolvedValue([master()])
    renderModal()

    await screen.findByText('Выберите услуги')
    ;(await screen.findByText('Стрижка')).click()
    ;(await screen.findByText('Окрашивание')).click()
    for (const s of extraServices) {
      ;(await screen.findByText(s.name)).click()
    }
    // That's already 6 (Стрижка + Окрашивание + 4 extras) — one over the limit.
    expect(await screen.findByText('За один визит можно выбрать не больше 5 услуг')).toBeInTheDocument()
  })
})

describe('ManualBookingModal — B2: "show the rest of the hours" toggle (Q7)', () => {
  async function toDateTimeStep() {
    getMasters.mockResolvedValue([master()])
    renderModal()

    await screen.findByText('Выберите услуги')
    ;(await screen.findByText('Стрижка')).click()
    ;(await screen.findByText('Продолжить')).click()

    // Pick a date so the time grid — and the toggle next to it — render.
    const dateButtons = await screen.findAllByRole('button', { name: /завтра/i })
    dateButtons[0].click()
  }

  it('does not send extendedHours until staff opts in', async () => {
    await toDateTimeStep()

    await screen.findByText('Показать остальные часы')
    const call = getSlots.mock.calls.find((c) => c[4] !== undefined) // date-bearing call
    expect(call?.[6]).toBe(false)
  })

  it('sends extendedHours=true once the toggle is clicked', async () => {
    await toDateTimeStep()

    const toggle = await screen.findByText('Показать остальные часы')
    getSlots.mockClear()
    toggle.click()

    await screen.findByText('Мастер в это время не работает — запись вне графика.')
    const call = getSlots.mock.calls[getSlots.mock.calls.length - 1]
    expect(call[5]).toBe(true) // manual
    expect(call[6]).toBe(true) // extendedHours
  })
})

describe('ManualBookingModal — US-64: empty master list matches BookingModal wording', () => {
  it('shows "Сейчас записаться нельзя" instead of an empty picker', async () => {
    getMasters.mockResolvedValue([])
    renderModal()

    await screen.findByText('Выберите услуги')
    ;(await screen.findByText('Стрижка')).click()
    ;(await screen.findByText('Продолжить')).click()

    expect(await screen.findByText('Сейчас записаться нельзя')).toBeInTheDocument()
  })
})
