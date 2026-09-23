import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { format, endOfMonth, startOfMonth } from 'date-fns'
import { ru } from 'date-fns/locale'
import { BookingCalendar } from './BookingCalendar'
import type { AvailabilityResponse } from '../../api/bookings'

const getAvailability = vi.fn()

vi.mock('../../api/bookings', async () => {
  const actual = await vi.importActual<typeof import('../../api/bookings')>('../../api/bookings')
  return {
    ...actual,
    bookingsApi: {
      getAvailability: (...args: unknown[]) => getAvailability(...args),
    },
  }
})

function renderCalendar() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <BookingCalendar
        companyId="co1"
        masterId="m1"
        serviceId="svc1"
        selectedDate=""
        onSelectDate={() => {}}
      />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getAvailability.mockReset()
})

describe('BookingCalendar — short booking horizon (defect fix)', () => {
  it('retries with a clamped `to` instead of crashing when the horizon is shorter than the displayed month', async () => {
    const today = new Date()
    const horizonDays = 3 // company set a horizon far shorter than "rest of this month"
    const horizonLastDate = new Date(today)
    horizonLastDate.setDate(horizonLastDate.getDate() + horizonDays)
    const horizonLastDateStr = format(horizonLastDate, 'yyyy-MM-dd')

    // First call — the natural month range — is rejected exactly as the server would reject it.
    getAvailability.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 400, data: `Записаться можно не дальше чем на ${horizonDays} дней вперёд` },
    })
    // Second call — clamped to the horizon — succeeds.
    const okResponse: AvailabilityResponse = {
      from: format(startOfMonth(today), 'yyyy-MM-dd'),
      to: horizonLastDateStr,
      totalDurationMinutes: 30,
      stepMinutes: 30,
      horizonDays,
      horizonLastDate: horizonLastDateStr,
      staffMode: false,
      days: [],
    }
    getAvailability.mockResolvedValueOnce(okResponse)

    renderCalendar()

    // No crash, no unhandled rejection surfacing as an error boundary — the calendar renders
    // normally once the clamped retry resolves.
    await waitFor(() => expect(getAvailability).toHaveBeenCalledTimes(2))

    const [, , , , , firstTo] = getAvailability.mock.calls[0]
    const [, , , , , secondTo] = getAvailability.mock.calls[1]
    expect(firstTo).toBe(format(endOfMonth(today), 'yyyy-MM-dd'))
    expect(secondTo).toBe(horizonLastDateStr)

    // The calendar is usable — e.g. the month header still renders — instead of stuck/broken.
    expect(await screen.findByText('Свободно')).toBeInTheDocument()
  })

  it('does not retry (and just surfaces the error) when the 400 is unrelated to the horizon', async () => {
    getAvailability.mockRejectedValue({
      isAxiosError: true,
      response: { status: 400, data: 'Мастер не оказывает услугу: Стрижка' },
    })

    renderCalendar()

    await waitFor(() => expect(getAvailability).toHaveBeenCalledTimes(1))
    // Give any accidental retry a chance to happen before asserting it didn't.
    await new Promise((r) => setTimeout(r, 50))
    expect(getAvailability).toHaveBeenCalledTimes(1)
  })
})

describe('BookingCalendar — review finding #4: a failed request must say so, not render a silent grey grid', () => {
  it('shows the server-provided message on an unrecognized error', async () => {
    getAvailability.mockRejectedValue({
      isAxiosError: true,
      response: { status: 400, data: 'Мастер не оказывает услугу: Стрижка' },
    })

    renderCalendar()

    expect(await screen.findByText('Мастер не оказывает услугу: Стрижка')).toBeInTheDocument()
  })

  it('shows a generic message when a 429/5xx carries no usable body', async () => {
    getAvailability.mockRejectedValue({ isAxiosError: true, response: { status: 429, data: undefined } })

    renderCalendar()

    expect(await screen.findByText('Не удалось загрузить доступное время. Попробуйте позже.')).toBeInTheDocument()
  })
})

describe('BookingCalendar — review finding #1: `lastFreeSlotStart` marks today as past once local time is beyond it', () => {
  it('does not let the client click "today" once local time passed the last free slot', async () => {
    const today = new Date()
    const pastMinutes = today.getHours() * 60 + today.getMinutes() - 1
    const lastFreeSlotStart = `${String(Math.max(0, Math.floor(pastMinutes / 60))).padStart(2, '0')}:${String(
      Math.max(0, pastMinutes % 60),
    ).padStart(2, '0')}:00`

    const todayStr = format(today, 'yyyy-MM-dd')
    getAvailability.mockResolvedValue({
      from: todayStr,
      to: format(endOfMonth(today), 'yyyy-MM-dd'),
      totalDurationMinutes: 30,
      stepMinutes: 30,
      horizonDays: 90,
      horizonLastDate: format(endOfMonth(today), 'yyyy-MM-dd'),
      staffMode: false,
      days: [{ date: todayStr, status: 'Available', lastFreeSlotStart, scheduleState: null }],
    })

    renderCalendar()

    await waitFor(() => expect(getAvailability).toHaveBeenCalled())
    const todayLabel = format(today, 'd MMMM', { locale: ru })
    const todayCell = await screen.findByRole('button', { name: new RegExp(`^${todayLabel}`, 'i') })
    expect(todayCell).toBeDisabled()
  })
})

describe('BookingCalendar — cycle 10: staffMode/scheduleState (ARCHITECTURE_CYCLE10.md §108.5)', () => {
  function renderManualCalendar() {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <QueryClientProvider client={qc}>
        <BookingCalendar
          companyId="co1"
          masterId="m1"
          serviceId="svc1"
          selectedDate=""
          onSelectDate={() => {}}
          manual
        />
      </QueryClientProvider>,
    )
  }

  it('makes a day-off day clickable, labelled "выходной", only when staffMode is true', async () => {
    const today = new Date()
    const tomorrow = format(new Date(today.getTime() + 86400000), 'yyyy-MM-dd')
    getAvailability.mockResolvedValue({
      from: format(startOfMonth(today), 'yyyy-MM-dd'),
      to: format(endOfMonth(today), 'yyyy-MM-dd'),
      totalDurationMinutes: 30,
      stepMinutes: 30,
      horizonDays: 90,
      horizonLastDate: format(endOfMonth(today), 'yyyy-MM-dd'),
      staffMode: true,
      days: [{ date: tomorrow, status: 'Available', lastFreeSlotStart: null, scheduleState: 'DayOff' }],
    })

    renderManualCalendar()

    const cell = await screen.findByRole('button', { name: /выходной/i })
    expect(cell).not.toBeDisabled()
  })

  it('a non-staff caller never sees a clickable "выходной" day (staffMode: false stays the old behaviour)', async () => {
    const today = new Date()
    const tomorrow = format(new Date(today.getTime() + 86400000), 'yyyy-MM-dd')
    getAvailability.mockResolvedValue({
      from: format(startOfMonth(today), 'yyyy-MM-dd'),
      to: format(endOfMonth(today), 'yyyy-MM-dd'),
      totalDurationMinutes: 30,
      stepMinutes: 30,
      horizonDays: 90,
      horizonLastDate: format(endOfMonth(today), 'yyyy-MM-dd'),
      staffMode: false,
      days: [{ date: tomorrow, status: 'DayOff', lastFreeSlotStart: null, scheduleState: null }],
    })

    renderCalendar()

    const cell = await screen.findByRole('button', { name: /выходной/i })
    expect(cell).toBeDisabled()
  })

  it('reports staffMode back to the parent via onStaffModeChange', async () => {
    const today = new Date()
    getAvailability.mockResolvedValue({
      from: format(startOfMonth(today), 'yyyy-MM-dd'),
      to: format(endOfMonth(today), 'yyyy-MM-dd'),
      totalDurationMinutes: 30,
      stepMinutes: 30,
      horizonDays: 90,
      horizonLastDate: format(endOfMonth(today), 'yyyy-MM-dd'),
      staffMode: true,
      days: [],
    })

    const onStaffModeChange = vi.fn()
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={qc}>
        <BookingCalendar
          companyId="co1"
          masterId="m1"
          serviceId="svc1"
          selectedDate=""
          onSelectDate={() => {}}
          manual
          onStaffModeChange={onStaffModeChange}
        />
      </QueryClientProvider>,
    )

    await waitFor(() => expect(onStaffModeChange).toHaveBeenCalledWith(true))
  })
})
