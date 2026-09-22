import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { format, endOfMonth, startOfMonth } from 'date-fns'
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
