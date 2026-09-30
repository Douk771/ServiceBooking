import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { CabinetHomePage } from './CabinetHomePage'

const my = vi.fn()
const useWebPush = vi.fn()
vi.mock('../../api/shops', () => ({ shopsApi: { my: () => my(), kindsSummary: vi.fn().mockRejectedValue(new Error('x')) } }))
vi.mock('@/hooks/useWebPush', () => ({ useWebPush: (...a: unknown[]) => useWebPush(...a) }))

const shop = (myRole: 'Owner' | 'Staff') => ({ id: 's1', name: 'Шаурма', slug: 'sh', logoUrl: null, isActive: true, myRole })
const pushBase = { isStaff: true, isLoading: false, reason: null, isSubscribedOnThisDevice: false }

function renderPage() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <CabinetHomePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  my.mockReset()
  useWebPush.mockReset().mockReturnValue({ ...pushBase })
})

describe('CabinetHomePage — cycle 33', () => {
  it('has no «Устройства и уведомления» button any more; owner sees «Подписка»', async () => {
    my.mockResolvedValue([shop('Owner')])
    renderPage()
    expect(await screen.findByText('Шаурма')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Устройства и уведомления/ })).toBeNull()
    expect(screen.getByRole('link', { name: /Подписка/ })).toBeInTheDocument()
  })

  it('staff member gets no empty «Аккаунт» nav', async () => {
    my.mockResolvedValue([shop('Staff')])
    renderPage()
    await screen.findByText('Шаурма')
    expect(screen.queryByRole('navigation', { name: 'Аккаунт' })).toBeNull()
  })

  it('nudges staff whose device has no notifications, linking to /profile#devices', async () => {
    my.mockResolvedValue([shop('Staff')])
    renderPage()
    const link = await screen.findByRole('link', { name: 'включить в профиле' })
    expect(link).toHaveAttribute('href', '/profile#devices')
  })

  it.each([
    ['already subscribed', { isSubscribedOnThisDevice: true }],
    ['not staff', { isStaff: false }],
    ['still loading', { isLoading: true }],
    ['push unavailable', { reason: 'platform-disabled' }],
  ])('no nudge when %s', async (_n, over) => {
    useWebPush.mockReturnValue({ ...pushBase, ...over })
    my.mockResolvedValue([shop('Staff')])
    renderPage()
    await screen.findByText('Шаурма')
    expect(screen.queryByTestId('push-nudge')).toBeNull()
  })
})
