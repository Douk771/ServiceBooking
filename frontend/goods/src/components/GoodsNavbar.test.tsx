import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { GoodsNavbar } from './GoodsNavbar'
import { useAuthStore } from '@/store/authStore'

const getGrid = vi.fn()
vi.mock('../api/ordersPricing', () => ({ ordersPricingApi: { get: (...a: unknown[]) => getGrid(...a) } }))
const unsubscribe = vi.fn()
vi.mock('@/hooks/useWebPush', () => ({ unsubscribeCurrentDeviceOnLogout: (...a: unknown[]) => unsubscribe(...a) }))

const user = { id: 'u1', firstName: 'Анна', lastName: 'И', email: 'a@b.c', roles: ['Client'] }

function renderNavbar() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <GoodsNavbar />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  unsubscribe.mockReset().mockResolvedValue(undefined)
  getGrid.mockReset().mockResolvedValue(null)
  useAuthStore.setState({ user: user as never, token: 'jwt' })
})

// ARCHITECTURE_CYCLE33.md §33.10.3 — logging out must drop the server push row BEFORE the token is cleared.
describe('GoodsNavbar logout', () => {
  it('removes the server push row (keeping the browser subscription) before clearing the token', async () => {
    let tokenAtCall: string | null = 'unset'
    unsubscribe.mockImplementation(async () => {
      tokenAtCall = useAuthStore.getState().token
    })
    renderNavbar()
    await userEvent.setup().click(screen.getAllByText('Выйти')[0])
    await waitFor(() => expect(useAuthStore.getState().token).toBeNull())
    expect(unsubscribe).toHaveBeenCalledWith({ keepBrowserSubscription: true })
    expect(tokenAtCall).toBe('jwt')
  })

  it('still logs out when the push cleanup fails', async () => {
    unsubscribe.mockRejectedValue(new Error('network'))
    renderNavbar()
    await userEvent.setup().click(screen.getAllByText('Выйти')[0])
    await waitFor(() => expect(useAuthStore.getState().token).toBeNull())
  })
})

describe('GoodsNavbar «Тарифы» (T37-15)', () => {
  it('shown on desktop and in the mobile menu when the grid exists', async () => {
    getGrid.mockResolvedValue({ version: 'v', currency: 'RUB', plans: [{ id: 'a', name: 'Лавка', description: null, pricePerMonth: 690, highlights: [], includedShops: 1, includedMembers: 5, includedProductsPerShop: 300, includedOrdersPerMonth: 1500, sortOrder: 1, isFree: false }], notice: '', legalNotice: null })
    renderNavbar()
    expect(await screen.findByRole('link', { name: 'Тарифы' })).toHaveAttribute('href', '/pricing')
    await userEvent.setup().click(screen.getByLabelText('Открыть меню'))
    await waitFor(() => expect(screen.getAllByRole('link', { name: 'Тарифы' })).toHaveLength(2))
  })
  it('absent on 404 in both places', async () => {
    renderNavbar()
    await waitFor(() => expect(getGrid).toHaveBeenCalled())
    await userEvent.setup().click(screen.getByLabelText('Открыть меню'))
    expect(screen.queryByRole('link', { name: 'Тарифы' })).toBeNull()
  })
})
