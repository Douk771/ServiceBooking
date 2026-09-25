import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { Booking } from '../../types'

const getSlots = vi.fn()
const reschedule = vi.fn()

vi.mock('../../api/bookings', async () => {
  const actual = await vi.importActual<typeof import('../../api/bookings')>('../../api/bookings')
  return {
    ...actual,
    bookingsApi: {
      ...actual.bookingsApi,
      getSlots: (...args: unknown[]) => getSlots(...args),
      reschedule: (...args: unknown[]) => reschedule(...args),
    },
  }
})

// Imported after the mock so the module under test picks it up.
const { RescheduleModal } = await import('./RescheduleModal')

const booking: Booking = {
  id: 'b1',
  companyId: 'co1',
  serviceId: 'svc1',
  serviceName: 'Стрижка',
  masterId: 'm1',
  masterName: 'Иван Петров',
  clientName: 'Пётр Сидоров',
  date: '2026-10-01',
  startTime: '10:00:00',
  endTime: '10:30:00',
  status: 'Confirmed' as Booking['status'],
  createdAt: '2026-09-01T00:00:00Z',
}

function renderModal(minHours?: number) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <RescheduleModal booking={booking} onClose={() => {}} minHours={minHours} />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getSlots.mockReset().mockResolvedValue([])
  reschedule.mockReset().mockResolvedValue({})
})

describe('RescheduleModal — F7: reads the time grid from the server, not a hand-rolled generator', () => {
  it('fetches slots via bookingsApi.getSlots with manual=true for the booking\'s master/service', async () => {
    renderModal()

    const dateButtons = await screen.findAllByRole('button', { name: /завтра/i })
    dateButtons[0].click()

    await screen.findByText('Показать остальные часы')
    const call = getSlots.mock.calls.find((c) => c[0] === 'co1')
    expect(call).toBeDefined()
    expect(call?.[1]).toBe('m1') // masterId
    expect(call?.[2]).toBe('svc1') // serviceId
    expect(call?.[5]).toBe(true) // manual
    expect(call?.[6]).toBe(false) // extendedHours, off by default
  })

  it('passes extendedHours=true once the "show the rest of the hours" toggle is used', async () => {
    renderModal()

    const dateButtons = await screen.findAllByRole('button', { name: /завтра/i })
    dateButtons[0].click()

    const toggle = await screen.findByText('Показать остальные часы')
    getSlots.mockClear()
    toggle.click()

    await screen.findByText('Мастер в это время не работает — запись вне графика.')
    const call = getSlots.mock.calls[getSlots.mock.calls.length - 1]
    expect(call[6]).toBe(true)
  })

  it('renders slots the server returns', async () => {
    getSlots.mockResolvedValue([{ start: '14:00:00', end: '14:30:00' }])
    renderModal()

    const dateButtons = await screen.findAllByRole('button', { name: /завтра/i })
    dateButtons[0].click()

    expect(await screen.findByText('14:00')).toBeInTheDocument()
  })
})

describe('RescheduleModal — US-17-01 (ARCHITECTURE_CYCLE17.md §305.1): one boundary for days and slots', () => {
  beforeEach(() => {
    // Fixed "now" so tomorrow-at-10:00 is a known, deterministic distance away.
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.setSystemTime(new Date('2026-10-01T12:00:00'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('filters out a tomorrow slot inside a 24h window (minHours=24)', async () => {
    getSlots.mockResolvedValue([{ start: '10:00:00', end: '10:30:00' }]) // tomorrow 10:00 < now+24h
    renderModal(24)

    const dateButtons = await screen.findAllByRole('button', { name: /завтра/i })
    dateButtons[0].click()

    expect(await screen.findByText('Нет доступных слотов на этот день')).toBeInTheDocument()
    expect(screen.queryByText('10:00')).not.toBeInTheDocument()
  })

  it('keeps a slot past the 24h boundary (minHours=24)', async () => {
    getSlots.mockResolvedValue([{ start: '13:00:00', end: '13:30:00' }]) // tomorrow 13:00 > now+24h
    renderModal(24)

    const dateButtons = await screen.findAllByRole('button', { name: /завтра/i })
    dateButtons[0].click()

    expect(await screen.findByText('13:00')).toBeInTheDocument()
  })

  it('minHours=0 (default) behaves as before this cycle — no extra filtering beyond "now"', async () => {
    getSlots.mockResolvedValue([{ start: '00:01:00', end: '00:31:00' }])
    renderModal(0)

    const dateButtons = await screen.findAllByRole('button', { name: /завтра/i })
    dateButtons[0].click()

    expect(await screen.findByText('00:01')).toBeInTheDocument()
  })
})
