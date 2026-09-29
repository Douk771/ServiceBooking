import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PlatformNoticeBanner } from './PlatformNoticeBanner'
import type { PlatformNoticeDto } from '../../api/platformNotices'

const getNotices = vi.fn()
const acknowledge = vi.fn()
const getAttachment = vi.fn()

vi.mock('../../api/platformNotices', () => ({
  platformNoticesApi: {
    getNotices: (...args: unknown[]) => getNotices(...args),
    acknowledge: (...args: unknown[]) => acknowledge(...args),
    getAttachment: (...args: unknown[]) => getAttachment(...args),
  },
}))

function notice(overrides: Partial<PlatformNoticeDto> = {}): PlatformNoticeDto {
  return {
    id: 'n1',
    kind: 'PriceChange',
    title: 'Изменение цены тарифа «Бизнес»',
    body: 'С 01.11.2026 меняется цена «Бизнес»: было 990 ₽, станет 1 190 ₽ за месяц.',
    linkUrl: null,
    effectiveFrom: '2026-11-01',
    publishedAt: '2026-09-29T09:00:00Z',
    visibleUntil: '2027-09-29T09:00:00Z',
    attachment: null,
    acknowledged: false,
    acknowledgedAt: null,
    revokedAt: null,
    ...overrides,
  }
}

function renderBanner() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <PlatformNoticeBanner />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getNotices.mockReset()
  acknowledge.mockReset()
  getAttachment.mockReset()
})

// API_CONTRACT_CYCLE20.md §434.1/§441 item 7 (US-20-03, Т20-02).
describe('PlatformNoticeBanner', () => {
  it('renders nothing when there are no pending notices', async () => {
    getNotices.mockResolvedValueOnce({ acknowledgeButtonText: 'Я ознакомился', acknowledgeCaption: 'подпись', items: [] })
    const { container } = renderBanner()
    await waitFor(() => expect(getNotices).toHaveBeenCalledWith('pending'))
    expect(container).toBeEmptyDOMElement()
  })

  it('shows every pending notice, not just the first, with a real <button> and its server-supplied caption', async () => {
    getNotices.mockResolvedValueOnce({
      acknowledgeButtonText: 'Я ознакомился',
      acknowledgeCaption: 'Это подтверждает только то, что вы прочитали сообщение, а не согласие с ним.',
      items: [notice({ id: 'n1', title: 'Первое' }), notice({ id: 'n2', title: 'Второе' })],
    })
    renderBanner()

    expect(await screen.findByText('Первое')).toBeInTheDocument()
    expect(screen.getByText('Второе')).toBeInTheDocument()
    const buttons = screen.getAllByRole('button', { name: 'Я ознакомился' })
    expect(buttons).toHaveLength(2)
    expect(screen.getAllByText('Это подтверждает только то, что вы прочитали сообщение, а не согласие с ним.')).toHaveLength(2)
    expect(screen.getAllByRole('status')).toHaveLength(2)
  })

  it('acknowledging calls the API and re-fetches "pending" (invalidates the shared query)', async () => {
    const user = userEvent.setup()
    let pending = [notice()]
    getNotices.mockImplementation(async () => ({ acknowledgeButtonText: 'Я ознакомился', acknowledgeCaption: '', items: pending }))
    acknowledge.mockImplementationOnce(async (id: string) => {
      pending = pending.filter((n) => n.id !== id)
      return notice({ acknowledged: true })
    })
    renderBanner()

    await user.click(await screen.findByRole('button', { name: 'Я ознакомился' }))
    await waitFor(() => expect(acknowledge).toHaveBeenCalledWith('n1'))
    await waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument())
  })

  it('shows the server error message when acknowledging fails', async () => {
    const user = userEvent.setup()
    getNotices.mockResolvedValue({ acknowledgeButtonText: 'Я ознакомился', acknowledgeCaption: '', items: [notice()] })
    acknowledge.mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: 'Уведомление отозвано.' } })
    renderBanner()

    await user.click(await screen.findByRole('button', { name: 'Я ознакомился' }))
    expect(await screen.findByText('Уведомление отозвано.')).toBeInTheDocument()
  })

  it('opens the attachment only inside a sandboxed iframe when "Читать новую редакцию" is clicked', async () => {
    const user = userEvent.setup()
    getNotices.mockResolvedValue({
      acknowledgeButtonText: 'Я ознакомился',
      acknowledgeCaption: '',
      items: [notice({ attachment: { title: 'Новая редакция', sha256: 'a'.repeat(64) } })],
    })
    getAttachment.mockResolvedValueOnce('<!doctype html><script>alert(1)</script><p>Текст</p>')
    renderBanner()

    await user.click(await screen.findByRole('button', { name: 'Читать новую редакцию' }))
    const iframe = await screen.findByTitle('Новая редакция')
    expect(iframe.tagName).toBe('IFRAME')
    expect(iframe).toHaveAttribute('sandbox', '')
    expect(iframe).not.toHaveAttribute('sandbox', 'allow-scripts')
  })
})
