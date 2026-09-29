import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NoticesPage } from './NoticesPage'
import type { PlatformNoticeDto } from '../api/platformNotices'

const getNotices = vi.fn()
const acknowledge = vi.fn()

vi.mock('../api/platformNotices', () => ({
  platformNoticesApi: {
    getNotices: (...args: unknown[]) => getNotices(...args),
    acknowledge: (...args: unknown[]) => acknowledge(...args),
    getAttachment: vi.fn(),
  },
}))

function notice(overrides: Partial<PlatformNoticeDto> = {}): PlatformNoticeDto {
  return {
    id: 'n1',
    kind: 'Other',
    title: 'Плановый перерыв',
    body: 'Сервис будет недоступен ночью.',
    linkUrl: null,
    effectiveFrom: null,
    publishedAt: '2026-09-29T09:00:00Z',
    visibleUntil: '2027-09-29T09:00:00Z',
    attachment: null,
    acknowledged: false,
    acknowledgedAt: null,
    revokedAt: null,
    ...overrides,
  }
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <NoticesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getNotices.mockReset()
  acknowledge.mockReset()
})

// API_CONTRACT_CYCLE20.md §434.1 scope=all (US-20-03).
describe('NoticesPage', () => {
  it('shows an empty state with no technical text when there are no notices', async () => {
    getNotices.mockResolvedValueOnce({ acknowledgeButtonText: 'Я ознакомился', acknowledgeCaption: '', items: [] })
    renderPage()
    expect(await screen.findByText('Уведомлений пока нет')).toBeInTheDocument()
    expect(getNotices).toHaveBeenCalledWith('all')
  })

  it('shows a "Прочитано" badge for an acknowledged notice, with no acknowledge button', async () => {
    getNotices.mockResolvedValueOnce({
      acknowledgeButtonText: 'Я ознакомился',
      acknowledgeCaption: '',
      items: [notice({ acknowledged: true, acknowledgedAt: '2026-09-29T10:00:00Z' })],
    })
    renderPage()
    expect(await screen.findByText('Прочитано')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Я ознакомился' })).not.toBeInTheDocument()
  })

  it('shows a dimmed "Отозвано" badge for a revoked notice, also with no acknowledge button', async () => {
    getNotices.mockResolvedValueOnce({
      acknowledgeButtonText: 'Я ознакомился',
      acknowledgeCaption: '',
      items: [notice({ revokedAt: '2026-09-29T12:00:00Z' })],
    })
    renderPage()
    expect(await screen.findByText('Отозвано')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Я ознакомился' })).not.toBeInTheDocument()
  })

  it('acknowledging an unread notice from the list calls the API', async () => {
    const user = userEvent.setup()
    getNotices.mockResolvedValue({ acknowledgeButtonText: 'Я ознакомился', acknowledgeCaption: 'подпись', items: [notice()] })
    acknowledge.mockResolvedValueOnce(notice({ acknowledged: true }))
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Я ознакомился' }))
    await waitFor(() => expect(acknowledge).toHaveBeenCalledWith('n1'))
  })

  it('shows a load-failure state distinct from the empty state', async () => {
    getNotices.mockRejectedValueOnce(new Error('network'))
    renderPage()
    expect(await screen.findByText('Не удалось загрузить уведомления.')).toBeInTheDocument()
  })
})
