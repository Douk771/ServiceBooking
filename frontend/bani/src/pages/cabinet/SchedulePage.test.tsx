import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SchedulePage } from './SchedulePage'

const schedule = vi.fn()
vi.mock('../../api/bathsCabinet', () => ({ bathsCabinetApi: { schedule: (...a: unknown[]) => schedule(...a) } }))
let perms: string[] = ['ViewSchedule']
vi.mock('../../cabinet/cabinetVertical', () => ({ useBathsCompany: () => ({ company: { id: 'c1', myPermissions: perms }, refresh: () => undefined }) }))

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <SchedulePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )

const SESSION = {
  sessionId: 's1',
  serviceName: 'Баня на дровах',
  timeLabel: 'пт 15 янв, 22:00 — сб 16 янв, 01:00',
  preparedUntilLabel: 'сб 16 янв, 01:30',
  guestName: 'Мария',
  guestsCount: 4,
  items: [{ name: 'Веник', quantity: 2 }],
  comment: null,
  paymentUnconfirmed: true,
}

describe('SchedulePage (bather)', () => {
  beforeEach(() => {
    schedule.mockReset()
    perms = ['ViewSchedule']
  })

  it('shows a session with the guests, the positions and the unconfirmed mark, and nothing about money or phones', async () => {
    schedule.mockResolvedValue({ today: '2026-01-15', days: [{ date: '2026-01-15', label: 'Сегодня, 15 января', sessions: [SESSION] }] })
    renderPage()
    expect(await screen.findByText('Баня на дровах')).toBeInTheDocument()
    expect(screen.getByText('Мария · 4 гостя')).toBeInTheDocument()
    expect(screen.getByText('Веник × 2')).toBeInTheDocument()
    expect(screen.getByText('оплата не подтверждена')).toBeInTheDocument()
    expect(screen.queryByText(/₽|тел|\+7/)).toBeNull()
    expect(schedule).toHaveBeenCalledWith('c1', { days: 14 })
  })

  it('shows the empty state when no day has a session', async () => {
    schedule.mockResolvedValue({ today: '2026-01-15', days: [{ date: '2026-01-15', label: 'Сегодня', sessions: [] }] })
    renderPage()
    expect(await screen.findByText('Сеансов нет', { selector: 'h3 ~ p, p' })).toBeInTheDocument()
  })

  it('shows an error with a retry', async () => {
    schedule.mockRejectedValue(new Error('boom'))
    renderPage()
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })

  it('refuses a member without ViewSchedule and does not ask the server', () => {
    perms = ['ViewBookings']
    renderPage()
    expect(screen.getByText('Раздел недоступен')).toBeInTheDocument()
    expect(schedule).not.toHaveBeenCalled()
  })
})
