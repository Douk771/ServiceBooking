import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BookingHistoryPanel } from './BookingHistoryPanel'
import type { BookingHistoryResponse } from '../../api/bookings'

const getHistory = vi.fn()

vi.mock('../../api/bookings', async () => {
  const actual = await vi.importActual<typeof import('../../api/bookings')>('../../api/bookings')
  return {
    ...actual,
    bookingsApi: { getHistory: (...args: unknown[]) => getHistory(...args) },
  }
})

function renderPanel(bookingId = 'b1') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <BookingHistoryPanel bookingId={bookingId} />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getHistory.mockReset()
})

describe('BookingHistoryPanel — API_CONTRACT_CYCLE10.md §122', () => {
  it('renders events newest-first, using the server-supplied title/actor label as-is', async () => {
    const response: BookingHistoryResponse = {
      bookingId: 'b1',
      precedesJournal: false,
      events: [
        {
          id: 'e1',
          kind: 'Created',
          occurredAt: '2026-09-01T10:00:00Z',
          title: 'Запись создана',
          actor: { kind: 'Client', name: 'Иван Иванов', role: null, label: 'Создал Иван Иванов' },
          reschedule: null,
          cancellationReason: null,
        },
        {
          id: 'e2',
          kind: 'Rescheduled',
          occurredAt: '2026-09-05T11:00:00Z',
          title: 'Запись перенесена',
          actor: { kind: 'Staff', name: 'Пётр Петров', role: 'Master', label: 'Перенёс Пётр Петров (мастер)' },
          reschedule: {
            fromDate: '2026-09-10',
            fromStartTime: '10:00:00',
            toDate: '2026-09-12',
            toStartTime: '14:00:00',
          },
          cancellationReason: null,
        },
      ],
    }
    getHistory.mockResolvedValue(response)

    renderPanel()

    const titles = await screen.findAllByText(/Запись (создана|перенесена)/)
    // Newest (Rescheduled, Sep 5) first — the panel's own display convention (§109.1), independent
    // of the server's ascending order.
    expect(titles[0]).toHaveTextContent('Запись перенесена')
    expect(titles[1]).toHaveTextContent('Запись создана')
    expect(screen.getByText('Перенёс Пётр Петров (мастер)')).toBeInTheDocument()
    // Review fix — the "было → стало" line now prints a human date (`d MMM`, ru locale), not the
    // raw `yyyy-MM-dd` the server sends.
    expect(screen.getByText(/10 сент\. 10:00 → 12 сент\. 14:00/)).toBeInTheDocument()
  })

  it('shows the "journal starts later" note only when precedesJournal is true', async () => {
    getHistory.mockResolvedValue({
      bookingId: 'b1',
      precedesJournal: true,
      events: [
        {
          id: 'e1',
          kind: 'Cancelled',
          occurredAt: '2026-09-05T11:00:00Z',
          title: 'Запись отменена',
          actor: { kind: 'Guest', name: 'Гость', role: null, label: 'Отменил гость' },
          reschedule: null,
          cancellationReason: 'Заболел',
        },
      ],
    })

    renderPanel()

    expect(await screen.findByText(/Журнал ведётся с определённого момента/)).toBeInTheDocument()
    expect(await screen.findByText('Причина: Заболел')).toBeInTheDocument()
  })

  it('shows a Russian error message instead of crashing when the request fails', async () => {
    getHistory.mockRejectedValue(new Error('network'))

    renderPanel()

    expect(await screen.findByText('Не удалось загрузить историю записи. Попробуйте позже.')).toBeInTheDocument()
  })
})
