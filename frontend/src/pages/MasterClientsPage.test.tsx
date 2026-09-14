import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MasterClientsPage } from './MasterClientsPage'
import type { MasterClient } from '../api/masters'
import type { Paged } from '../types'

const getClients = vi.fn()

vi.mock('../api/masters', () => ({
  mastersApi: {
    getClients: (...args: unknown[]) => getClients(...args),
    addNote: vi.fn(),
    deleteNote: vi.fn(),
  },
}))

beforeEach(() => {
  getClients.mockReset()
})

function renderWithClient(ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>)
}

function makeClient(overrides: Partial<MasterClient> = {}): MasterClient {
  return {
    clientId: 'c1',
    guestPhone: null,
    name: 'Анна Петрова',
    phone: '+79990000000',
    email: null,
    lastVisitDate: '2026-08-30',
    totalVisits: 2,
    notes: [],
    bookingSummaries: [],
    ...overrides,
  }
}

function page(items: MasterClient[], overrides: Partial<Paged<MasterClient>> = {}): Paged<MasterClient> {
  return { items, page: 1, pageSize: 20, total: items.length, hasNext: false, ...overrides }
}

describe('MasterClientsPage', () => {
  it('requests clients without a search param on first render', async () => {
    getClients.mockResolvedValueOnce(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await screen.findByText('Анна Петрова')
    expect(getClients).toHaveBeenCalledWith('co1', 1, 20, '')
  })

  it('debounces typing before issuing a server search request', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ delay: null, advanceTimers: vi.advanceTimersByTime })
    getClients.mockResolvedValue(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await waitFor(() => expect(getClients).toHaveBeenCalledTimes(1))

    const input = screen.getByPlaceholderText('Поиск по имени или телефону…')
    await user.type(input, 'Ир')

    // Still just the initial request — debounce hasn't elapsed yet.
    expect(getClients).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(400)
    await waitFor(() => expect(getClients).toHaveBeenCalledTimes(2))
    expect(getClients).toHaveBeenLastCalledWith('co1', 1, 20, 'Ир')

    vi.useRealTimers()
  })

  it('resets to page 1 when the search term changes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ delay: null, advanceTimers: vi.advanceTimersByTime })
    getClients.mockResolvedValue(page(Array.from({ length: 20 }, (_, i) => makeClient({ clientId: `c${i}` }))))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    await waitFor(() => expect(getClients).toHaveBeenCalledTimes(1))
    expect(getClients).toHaveBeenLastCalledWith('co1', 1, 20, '')

    const input = screen.getByPlaceholderText('Поиск по имени или телефону…')
    await user.type(input, 'Client on page 2')
    await vi.advanceTimersByTimeAsync(400)

    await waitFor(() => expect(getClients).toHaveBeenLastCalledWith('co1', 1, 20, 'Client on page 2'))

    vi.useRealTimers()
  })

  it('shows "not found" when a search yields no results, without hiding the count elsewhere', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ delay: null, advanceTimers: vi.advanceTimersByTime })
    getClients.mockResolvedValueOnce(page([makeClient()]))
    renderWithClient(<MasterClientsPage companyId="co1" />)
    await screen.findByText('Анна Петрова')

    getClients.mockResolvedValueOnce(page([], { total: 0 }))
    const input = screen.getByPlaceholderText('Поиск по имени или телефону…')
    await user.type(input, 'Несуществующий')
    await vi.advanceTimersByTimeAsync(400)

    expect(await screen.findByText('Клиентов не найдено')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('shows the empty-state without a search hint when there is no search term', async () => {
    getClients.mockResolvedValueOnce(page([], { total: 0 }))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    expect(await screen.findByText('У вас пока нет клиентов')).toBeInTheDocument()
    expect(screen.getByText('Здесь появятся клиенты после первых записей')).toBeInTheDocument()
  })

  it('shows an error state on API failure', async () => {
    getClients.mockRejectedValueOnce(new Error('network error'))
    renderWithClient(<MasterClientsPage companyId="co1" />)

    expect(await screen.findByText('Не удалось загрузить клиентов')).toBeInTheDocument()
  })
})
