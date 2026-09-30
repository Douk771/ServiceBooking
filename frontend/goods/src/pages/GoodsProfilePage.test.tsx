import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { GoodsProfilePage } from './GoodsProfilePage'
import { useAuthStore } from '@/store/authStore'

const unsubscribe = vi.fn()
const useWebPush = vi.fn()
vi.mock('@/hooks/useWebPush', () => ({
  useWebPush: (...a: unknown[]) => useWebPush(...a),
  describeDevice: () => 'Chrome на Android',
  unsubscribeCurrentDeviceOnLogout: (...a: unknown[]) => unsubscribe(...a),
}))
vi.mock('@/api/profile', () => ({
  profileApi: { get: vi.fn().mockResolvedValue({ firstName: 'Анна', lastName: 'И', phone: null, email: 'a@b.c' }) },
}))
vi.mock('@/hooks/usePhoneVerification', () => ({ usePhoneVerificationConfig: () => ({ data: undefined }) }))
vi.mock('../api/shops', () => ({ shopsApi: { kindsSummary: vi.fn().mockRejectedValue(new Error('x')) } }))
vi.mock('../components/staffMax/StaffMaxCard', () => ({ StaffMaxCard: () => <div data-testid="staff-max" /> }))

const shop = { companyId: 'c2', companyName: 'Шаурма', staffPushEnabled: true, kind: 'Orders' as const }
const pushBase = {
  reason: null, isLoading: false, isSubscribedOnThisDevice: false, devices: [], isEnabling: false, isDisabling: false,
  actionError: null, companies: [shop], hasServices: false, hasOrders: true, isStaff: true, devicesError: false,
  enableOnThisDevice: vi.fn(), disableOnThisDevice: vi.fn(), disableDevice: vi.fn(),
}

function renderPage() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <GoodsProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  unsubscribe.mockReset().mockResolvedValue(undefined)
  useWebPush.mockReset().mockReturnValue({ ...pushBase })
  useAuthStore.setState({ user: { id: 'u1', firstName: 'Анна', lastName: 'И', email: 'a@b.c', roles: ['Client'] } as never, token: 'jwt' })
})

describe('GoodsProfilePage — cycle 33', () => {
  it('shows «Устройства и уведомления» (with the MAX card) to shop staff', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Устройства и уведомления' })).toBeInTheDocument()
    expect(screen.getByTestId('staff-max')).toBeInTheDocument()
    expect(useWebPush).toHaveBeenCalledWith({ site: 'Orders', keepBrowserSubscription: true })
  })

  it('hides the section from a plain buyer', async () => {
    useWebPush.mockReturnValue({ ...pushBase, isStaff: false, companies: [], hasOrders: false })
    renderPage()
    await screen.findByText('Данные и согласия')
    expect(screen.queryByRole('heading', { name: 'Устройства и уведомления' })).toBeNull()
    expect(screen.queryByTestId('staff-max')).toBeNull()
  })

  it('«Выйти» drops the server push row before the token, and logs out even if that fails', async () => {
    let tokenAtCall: string | null = 'unset'
    unsubscribe.mockImplementation(async () => {
      tokenAtCall = useAuthStore.getState().token
      throw new Error('network')
    })
    renderPage()
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Выйти' }))
    await waitFor(() => expect(useAuthStore.getState().token).toBeNull())
    expect(unsubscribe).toHaveBeenCalledWith({ keepBrowserSubscription: true })
    expect(tokenAtCall).toBe('jwt')
  })
})
