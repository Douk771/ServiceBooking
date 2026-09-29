import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BillingNoticesSummary } from './BillingNoticesSummary'
import type { PlatformNoticeDto } from '../../api/platformNotices'

const getNotices = vi.fn()
vi.mock('../../api/platformNotices', () => ({
  platformNoticesApi: { getNotices: (...args: unknown[]) => getNotices(...args) },
}))

function notice(overrides: Partial<PlatformNoticeDto> = {}): PlatformNoticeDto {
  return {
    id: 'n1',
    kind: 'Other',
    title: 'Заголовок',
    body: 'Тело',
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

function renderSummary() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <BillingNoticesSummary />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => getNotices.mockReset())

describe('BillingNoticesSummary', () => {
  it('renders nothing when there are no notices at all', async () => {
    getNotices.mockResolvedValueOnce({ acknowledgeButtonText: '', acknowledgeCaption: '', items: [] })
    const { container } = renderSummary()
    await waitFor(() => expect(getNotices).toHaveBeenCalledWith('all'))
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the unread count badge and links to /notices', async () => {
    getNotices.mockResolvedValueOnce({
      acknowledgeButtonText: '',
      acknowledgeCaption: '',
      items: [notice({ id: 'n1', acknowledged: false }), notice({ id: 'n2', acknowledged: true })],
    })
    renderSummary()
    expect(await screen.findByText('1 новых')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Все уведомления' })).toHaveAttribute('href', '/notices')
  })

  it('does not count a revoked notice as unread', async () => {
    getNotices.mockResolvedValueOnce({
      acknowledgeButtonText: '',
      acknowledgeCaption: '',
      items: [notice({ acknowledged: false, revokedAt: '2026-09-29T10:00:00Z' })],
    })
    renderSummary()
    await screen.findByText('Уведомления сервиса')
    expect(screen.queryByText(/новых/)).not.toBeInTheDocument()
  })
})
