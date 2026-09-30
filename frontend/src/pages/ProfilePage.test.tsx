import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { ProfilePage } from './ProfilePage'

const profileGet = vi.fn()
const useWebPush = vi.fn()

vi.mock('../api/profile', () => ({ profileApi: { get: () => profileGet(), update: vi.fn(), uploadAvatar: vi.fn() } }))
vi.mock('../api/notifications', () => ({ notificationsApi: { getPreferences: vi.fn().mockResolvedValue({}), updatePreferences: vi.fn() } }))
vi.mock('../hooks/useExportData', () => ({ useExportData: () => ({ exportMut: { isPending: false, mutate: vi.fn() }, exportError: null, gateNotice: null }) }))
vi.mock('../hooks/usePhoneVerification', () => ({ usePhoneVerificationConfig: () => ({ data: undefined }) }))
vi.mock('../components/profile/GuestDataGateNotice', () => ({ GuestDataGateNotice: () => null }))
vi.mock('../hooks/useWebPush', () => ({
  useWebPush: (...a: unknown[]) => useWebPush(...a),
  describeDevice: () => 'Chrome · Mac',
}))

const pushBase = {
  isStaff: true, hasServices: true, hasOrders: false, isLoading: false, reason: null,
  isSubscribedOnThisDevice: false, isEnabling: false, isDisabling: false, devices: [], devicesError: false,
  actionError: null, enableOnThisDevice: vi.fn(), disableOnThisDevice: vi.fn(), disableDevice: vi.fn(),
}

function renderPage() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  profileGet.mockReset().mockResolvedValue({
    id: 'u1', email: 'a@b.c', firstName: 'Анна', lastName: 'Петрова', phone: null, role: 'Master', roles: ['Master'],
  })
  useWebPush.mockReset().mockReturnValue({ ...pushBase })
})

describe('ProfilePage — «Устройства и уведомления» (US-33-02)', () => {
  it('shows the block to staff', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Устройства и уведомления' })).toBeInTheDocument()
    expect(useWebPush).toHaveBeenCalledWith(expect.objectContaining({ site: 'Services' }))
  })

  it('does not show the block to a client without a company role', async () => {
    useWebPush.mockReturnValue({ ...pushBase, isStaff: false, hasServices: false })
    renderPage()
    await screen.findByDisplayValue('Анна').catch(() => undefined)
    expect(screen.queryByRole('heading', { name: 'Устройства и уведомления' })).toBeNull()
  })
})
