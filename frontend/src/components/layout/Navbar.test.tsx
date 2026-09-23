import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { Navbar } from './Navbar'
import { useAuthStore } from '../../store/authStore'

const navigate = vi.fn()
const unsubscribeCurrentDeviceOnLogout = vi.fn()

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>()
  return { ...actual, useNavigate: () => navigate }
})

vi.mock('../../hooks/useWebPush', () => ({
  unsubscribeCurrentDeviceOnLogout: (...args: unknown[]) => unsubscribeCurrentDeviceOnLogout(...args),
}))

function renderNavbar() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <Navbar />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Navbar — logout (§105.5 rubezh 2)', () => {
  beforeEach(() => {
    navigate.mockReset()
    unsubscribeCurrentDeviceOnLogout.mockReset()
    useAuthStore.setState({
      user: { id: 'u1', firstName: 'А', lastName: 'Б', roles: ['Client'] } as never,
      token: 'a-token',
    })
  })

  it('awaits unsubscribeCurrentDeviceOnLogout() and finishes it BEFORE the token is cleared, so the DELETE still carries Authorization', async () => {
    const order: string[] = []
    unsubscribeCurrentDeviceOnLogout.mockImplementation(async () => {
      // Simulate the real implementation's microtask hops (await getRegistration(), await
      // getSubscription()) before it ever reaches the network call.
      await Promise.resolve()
      await Promise.resolve()
      order.push('unsubscribe-finished')
      // The token must still be present while unsubscribeCurrentDeviceOnLogout is still running —
      // this is the exact bug (Б2): logout() used to clear it in parallel via a fire-and-forget call.
      expect(useAuthStore.getState().token).toBe('a-token')
    })

    const user = userEvent.setup()
    renderNavbar()

    await user.click(screen.getByRole('button', { name: /выйти/i }))

    await vi.waitFor(() => expect(order).toEqual(['unsubscribe-finished']))

    // Only cleared once that call has actually resolved, not merely been fired-and-forgotten.
    expect(useAuthStore.getState().token).toBeNull()
    expect(unsubscribeCurrentDeviceOnLogout).toHaveBeenCalledTimes(1)
  })

  it('still logs out and navigates home even if unsubscribeCurrentDeviceOnLogout rejects unexpectedly', async () => {
    unsubscribeCurrentDeviceOnLogout.mockRejectedValue(new Error('network down'))
    const user = userEvent.setup()
    renderNavbar()

    await user.click(screen.getByRole('button', { name: /выйти/i }))

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/'))
    expect(useAuthStore.getState().token).toBeNull()
  })
})
