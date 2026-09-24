import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NotificationLogTab } from './NotificationLogTab'
import type { NotificationLogEntry, Paged } from '../../types'

const getLog = vi.fn()
const getLogSummary = vi.fn()

vi.mock('../../api/notifications', () => ({
  notificationsApi: {
    getLog: (...args: unknown[]) => getLog(...args),
    getLogSummary: (...args: unknown[]) => getLogSummary(...args),
  },
}))

function page(items: NotificationLogEntry[]): Paged<NotificationLogEntry> {
  return { items, page: 1, pageSize: 20, total: items.length, hasNext: false }
}

function entry(overrides: Partial<NotificationLogEntry> = {}): NotificationLogEntry {
  return {
    id: 'n1',
    createdAt: '2026-09-22T10:00:00Z',
    type: 'BookingConfirmed',
    transport: 'WhatsApp',
    typeText: 'Подтверждение записи',
    recipientName: 'Мария',
    recipientPhoneMasked: '+7 *** *** 12 34',
    status: 'Delivered',
    statusText: 'Доставлено',
    bookingId: 'b1',
    visitStart: null,
    sentAt: null,
    channelId: 'ch1',
    contentRedacted: false,
    ...overrides,
  }
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <NotificationLogTab companyId="c1" />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getLog.mockReset()
  getLogSummary.mockReset()
  getLogSummary.mockResolvedValue(null)
})

describe('NotificationLogTab — transport filter (§114.3)', () => {
  it('shows a transport badge per row and re-queries with the selected ?transport= filter', async () => {
    const user = userEvent.setup()
    getLog.mockResolvedValue(page([entry({ transport: 'Max' })]))
    renderTab()

    expect(await screen.findByText('MAX')).toBeInTheDocument()
    await waitFor(() => expect(getLog).toHaveBeenCalledWith('c1', { page: 1, pageSize: 20, status: undefined, transport: undefined }))

    getLog.mockClear()
    getLog.mockResolvedValue(page([entry({ transport: 'Max' })]))
    await user.selectOptions(screen.getByLabelText('Канал'), 'Max')

    await waitFor(() => expect(getLog).toHaveBeenCalledWith('c1', { page: 1, pageSize: 20, status: undefined, transport: 'Max' }))
  })
})
