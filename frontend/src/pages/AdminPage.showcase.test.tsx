import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AdminPage } from './AdminPage'
import type { AdminCompany, AdminUser } from '../api/admin'

// Cycle 28 (API_CONTRACT_CYCLE28.md §594, ARCHITECTURE_CYCLE28.md §578): «Витрина» badge, the three-way filter and the
// separate summary line on the dashboard. Only the admin API is mocked.

const getStats = vi.fn()
const getUsers = vi.fn()
const getCompanies = vi.fn()

vi.mock('../api/admin', () => ({
  adminApi: {
    getStats: (...a: unknown[]) => getStats(...a),
    getUsers: (...a: unknown[]) => getUsers(...a),
    getCompanies: (...a: unknown[]) => getCompanies(...a),
  },
}))

const STATS = {
  totalCompanies: 3,
  totalUsers: 5,
  totalBookings: 7,
  completedBookings: 4,
  totalRevenue: 12000,
  showcaseCompanies: 9,
  showcaseUsers: 158,
  showcaseBookings: 9412,
}

function company(over: Partial<AdminCompany>): AdminCompany {
  return {
    id: 'c1',
    name: 'Салон',
    slug: 'salon',
    kind: 'Services',
    isActive: true,
    allowSelfBooking: true,
    createdAt: '2026-09-01T00:00:00Z',
    memberCount: 1,
    bookingCount: 0,
    ownerUserId: 'u1',
    ownerEmail: 'o@example.com',
    planName: 'Бесплатный',
    subscriptionActive: false,
    ...over,
  }
}

function user(over: Partial<AdminUser>): AdminUser {
  return {
    id: 'u1',
    phone: '79001234567',
    firstName: 'Иван',
    lastName: 'Иванов',
    createdAt: '2026-09-01T00:00:00Z',
    roles: ['Client'],
    ownedCompanyCount: 0,
    planName: 'Бесплатный',
    subscriptionActive: false,
    ...over,
  }
}

function renderAdmin() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <AdminPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, total: items.length, hasNext: false })

beforeEach(() => {
  getStats.mockReset().mockResolvedValue(STATS)
  getUsers.mockReset().mockResolvedValue(paged([]))
  getCompanies.mockReset().mockResolvedValue(paged([]))
})

describe('AdminPage — dashboard', () => {
  it('keeps the old tiles and adds a separate «Витрина: N компаний, M записей» line', async () => {
    renderAdmin()

    expect(await screen.findByText(/Витрина: 9 компаний, 9\s412 записей/)).toBeInTheDocument()
    expect(screen.getByText('Компаний').previousElementSibling).toHaveTextContent('3')
  })

  it('no showcase line when the server sent no counters (older server) or the showcase is empty', async () => {
    getStats.mockResolvedValue({ totalCompanies: 3, totalUsers: 5, totalBookings: 7, completedBookings: 4, totalRevenue: 0 })
    renderAdmin()

    await screen.findByText('Компаний')
    expect(screen.queryByText(/Витрина:/)).not.toBeInTheDocument()
  })

  it('a failed stats request shows an error, not a dashboard of zeros', async () => {
    getStats.mockRejectedValue(new Error('500'))
    renderAdmin()

    expect(await screen.findByText(/Не удалось загрузить сводку/)).toBeInTheDocument()
    expect(screen.queryByText('Компаний')).not.toBeInTheDocument()
  })
})

describe('AdminPage — companies tab', () => {
  it('badges only showcase companies', async () => {
    getCompanies.mockResolvedValue(
      paged([
        company({ id: 'c1', name: 'Витринный салон', isShowcase: true, showcaseBookingOpen: true }),
        company({ id: 'c2', name: 'Настоящий салон', isShowcase: false }),
      ]),
    )
    const user_ = userEvent.setup()
    renderAdmin()
    await user_.click(screen.getByRole('button', { name: 'Компании' }))

    const showcaseRow = (await screen.findByText('Витринный салон')).closest('div.p-4') as HTMLElement
    const realRow = screen.getByText('Настоящий салон').closest('div.p-4') as HTMLElement
    expect(showcaseRow).toHaveTextContent('Витрина')
    expect(realRow).not.toHaveTextContent('Витрина')
  })

  it('«Все» sends no showcase param, «Только витрина» / «Без витрины» send only / exclude and go back to page 1', async () => {
    const user_ = userEvent.setup()
    renderAdmin()
    await user_.click(screen.getByRole('button', { name: 'Компании' }))
    await waitFor(() => expect(getCompanies).toHaveBeenCalled())

    const last = () => getCompanies.mock.calls[getCompanies.mock.calls.length - 1]
    // args: (search, page, pageSize, kind, showcase)
    expect(last()[4]).toBeUndefined()

    await user_.click(screen.getByRole('button', { name: 'Только витрина' }))
    await waitFor(() => expect(last()[4]).toBe('only'))
    expect(last()[1]).toBe(1)

    await user_.click(screen.getByRole('button', { name: 'Без витрины' }))
    await waitFor(() => expect(last()[4]).toBe('exclude'))
  })

  it('a failed load shows an error instead of «Компаний не найдено»', async () => {
    getCompanies.mockRejectedValue(new Error('500'))
    const user_ = userEvent.setup()
    renderAdmin()
    await user_.click(screen.getByRole('button', { name: 'Компании' }))

    expect(await screen.findByText(/Не удалось загрузить компании/)).toBeInTheDocument()
    expect(screen.queryByText('Компаний не найдено')).not.toBeInTheDocument()
  })
})

describe('AdminPage — users tab', () => {
  it('badges only showcase users and passes the filter as the 4th argument', async () => {
    getUsers.mockResolvedValue(
      paged([user({ id: 'u1', firstName: 'Витрина', lastName: 'Клиент', isShowcase: true }), user({ id: 'u2', firstName: 'Настоящий', lastName: 'Клиент' })]),
    )
    const user_ = userEvent.setup()
    renderAdmin()
    await user_.click(screen.getByRole('button', { name: 'Пользователи' }))

    const showcaseRow = (await screen.findByText('Витрина Клиент')).closest('div.p-3') as HTMLElement
    const realRow = screen.getByText('Настоящий Клиент').closest('div.p-3') as HTMLElement
    expect(showcaseRow).toHaveTextContent('Витрина')
    expect(realRow).not.toHaveTextContent('Витрина')

    const last = () => getUsers.mock.calls[getUsers.mock.calls.length - 1]
    expect(last()[3]).toBeUndefined()
    await user_.click(screen.getByRole('button', { name: 'Только витрина' }))
    await waitFor(() => expect(last()[3]).toBe('only'))
  })

  it('an empty result says so', async () => {
    const user_ = userEvent.setup()
    renderAdmin()
    await user_.click(screen.getByRole('button', { name: 'Пользователи' }))

    expect(await screen.findByText('Пользователей не найдено')).toBeInTheDocument()
  })
})
