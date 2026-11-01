import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { Navbar } from './Navbar'
import { useAuthStore } from '../../store/authStore'

const navigate = vi.fn()
const unsubscribeCurrentDeviceOnLogout = vi.fn()
const getMy = vi.fn()
vi.mock('../../api/companies', () => ({ companiesApi: { getMy: () => getMy() } }))

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

// Cycle 49 (API_CONTRACT_CYCLE49 §49.27.1, CY49-F3) — «Подписка» for salon owners.
describe('Navbar — «Подписка» (cycle 49)', () => {
  const setUser = (roles: string[] | null) =>
    useAuthStore.setState(roles ? { user: { id: 'u1', firstName: 'А', lastName: 'Б', roles } as never, token: 't' } : { user: null, token: null })

  function renderAt(path = '/') {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={[path]}>
          <Navbar />
        </MemoryRouter>
      </QueryClientProvider>,
    )
  }

  beforeEach(() => {
    getMy.mockReset().mockResolvedValue([])
  })

  const matrix: [string, string[] | null, unknown[], boolean][] = [
    ['guest', null, [], false],
    ['client', ['Client'], [], false],
    ['master without a salon', ['Master'], [], false],
    ['owner of a shop/house/bath only', ['CompanyOwner'], [], false],
    ['SuperAdmin without a salon', ['SuperAdmin'], [], false],
    ['salon owner', ['CompanyOwner'], [{ id: 'c1' }], true],
    ['master of salon A and owner of salon B', ['Master', 'CompanyOwner'], [{ id: 'c1' }], true],
    ['SuperAdmin with own salon', ['SuperAdmin', 'CompanyOwner'], [{ id: 'c1' }], true],
  ]

  it.each(matrix)('wide header, %s', async (_n, roles, companies, visible) => {
    setUser(roles)
    getMy.mockResolvedValue(companies)
    renderAt()
    if (visible) expect(await screen.findByRole('link', { name: 'Подписка' })).toHaveAttribute('href', '/billing')
    else {
      await new Promise((r) => setTimeout(r, 20))
      expect(screen.queryByRole('link', { name: 'Подписка' })).toBeNull()
    }
  })

  it.each(matrix)('mobile menu, %s', async (_n, roles, companies, visible) => {
    setUser(roles)
    getMy.mockResolvedValue(companies)
    renderAt()
    await userEvent.click(screen.getByRole('button', { name: 'Открыть меню' }))
    const links = () => screen.queryAllByRole('link', { name: 'Подписка' })
    if (visible) await vi.waitFor(() => expect(links().length).toBeGreaterThan(0))
    else {
      await new Promise((r) => setTimeout(r, 20))
      expect(links()).toHaveLength(0)
    }
  })

  it('does not call /companies/my without the CompanyOwner role', async () => {
    setUser(['Master', 'Client'])
    renderAt()
    await new Promise((r) => setTimeout(r, 20))
    expect(getMy).not.toHaveBeenCalled()
  })

  it('is hidden while the request fails', async () => {
    setUser(['CompanyOwner'])
    getMy.mockRejectedValue(new Error('x'))
    renderAt()
    await vi.waitFor(() => expect(getMy).toHaveBeenCalled())
    expect(screen.queryByRole('link', { name: 'Подписка' })).toBeNull()
  })

  it('stands right after «Кабинет» and is aria-current on /billing', async () => {
    setUser(['CompanyOwner'])
    getMy.mockResolvedValue([{ id: 'c1' }])
    renderAt('/billing')
    const sub = await screen.findByRole('link', { name: 'Подписка' })
    expect(sub).toHaveAttribute('aria-current', 'page')
    const cab = screen.getByRole('link', { name: 'Кабинет' })
    expect(cab.compareDocumentPosition(sub) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(cab.nextElementSibling).toBe(sub)
  })

  it('a click in the mobile menu closes it', async () => {
    setUser(['CompanyOwner'])
    getMy.mockResolvedValue([{ id: 'c1' }])
    renderAt()
    await userEvent.click(screen.getByRole('button', { name: 'Открыть меню' }))
    const links = await vi.waitFor(() => {
      const l = screen.getAllByRole('link', { name: 'Подписка' })
      expect(l).toHaveLength(2)
      return l
    })
    await userEvent.click(links[1])
    expect(screen.getByRole('button', { name: 'Открыть меню' })).toBeInTheDocument()
  })
})
