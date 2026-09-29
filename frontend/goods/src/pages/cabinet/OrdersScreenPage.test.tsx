import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { OrdersScreenPage } from './OrdersScreenPage'
import { staffCard, staffOrder } from '../../components/orders/testData'
import type { OrderBoardDto, ShopManageDto } from '../../types'

const board = vi.fn()
const accept = vi.fn()
vi.mock('../../api/orders', () => ({
  ordersApi: { board: (...a: unknown[]) => board(...a), accept: (...a: unknown[]) => accept(...a) },
}))
vi.mock('../../api/catalog', () => ({ catalogApi: { products: () => Promise.resolve([]) } }))

const shop = {
  id: 's1', name: 'Шаурма', myRole: 'Staff', isActive: true,
  settings: { customerMode: 'Anyone', acceptanceMode: 'Manual', allowCustomerCancel: true, trackStock: false },
} as unknown as ShopManageDto

function fullBoard(over: Partial<OrderBoardDto> = {}): OrderBoardDto {
  return { revision: 1, changed: true, businessDate: '2026-10-05', serverTimeUtc: '2026-10-05T10:05:00Z', newOrders: [staffCard()], accepted: [], ready: [], completedToday: [], ...over }
}

function Layout() {
  return <Outlet context={{ shop, isOwner: false }} />
}

function renderScreen() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/s1/orders']}>
        <Routes>
          <Route element={<Layout />}>
            <Route path="/cabinet/:shopId/orders" element={<OrdersScreenPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const column = (name: string) => screen.getByRole('region', { name })

beforeEach(() => {
  board.mockReset()
  accept.mockReset()
})
afterEach(() => vi.useRealTimers())

describe('OrdersScreenPage', () => {
  it('puts orders in their columns, shows the freshness line and the count of new orders in the tab title', async () => {
    board.mockResolvedValue(fullBoard({ accepted: [staffCard({ id: 'o9', number: 9, status: 'Accepted', availableActions: ['MarkReady'] })] }))
    renderScreen()
    await screen.findByTestId('order-card-27')
    expect(within(column('Новые')).getByTestId('order-card-27')).toBeInTheDocument()
    expect(within(column('Принятые')).getByTestId('order-card-9')).toBeInTheDocument()
    expect(screen.getByTestId('freshness')).toHaveTextContent(/Обновлено \d+ с назад/)
    await waitFor(() => expect(document.title).toBe('(1) Заказы — Шаурма'))
    // first poll has no revision, so the server sends the full board
    expect(board).toHaveBeenCalledWith('s1', {})
  })

  it('on 409 VersionMismatch replaces the card with the order from the body, says why, and does not repeat the action', async () => {
    // the other staff member accepted it meanwhile: the follow-up poll (revision moved) shows it in «Принятые»
    board
      .mockResolvedValueOnce(fullBoard())
      .mockResolvedValue(fullBoard({ revision: 2, newOrders: [], accepted: [staffCard({ status: 'Accepted', version: 4, availableActions: ['MarkReady'] })] }))
    accept.mockRejectedValue({
      response: { status: 409, data: { code: 'VersionMismatch', message: 'Заказ уже изменён — вот актуальное состояние', order: staffOrder({ status: 'Accepted', version: 4, availableActions: ['MarkReady'] }) } },
    })
    const user = userEvent.setup()
    renderScreen()
    await user.click(await screen.findByRole('button', { name: 'Принять' }))
    expect(await screen.findByText(/Заказ № 27: Заказ уже изменён — вот актуальное состояние/)).toBeInTheDocument()
    expect(within(column('Принятые')).getByTestId('order-card-27')).toBeInTheDocument()
    expect(within(column('Новые')).queryByTestId('order-card-27')).toBeNull()
    expect(accept).toHaveBeenCalledTimes(1)
    expect(accept).toHaveBeenCalledWith('s1', 'o1', 3)
  })

  it('polls again with the last revision and highlights an order that appeared in between', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    board
      .mockResolvedValueOnce(fullBoard())
      .mockResolvedValue(fullBoard({ revision: 2, newOrders: [staffCard(), staffCard({ id: 'o2', number: 28, createdAtUtc: '2026-10-05T10:04:00Z' })] }))
    renderScreen()
    await screen.findByTestId('order-card-27')
    expect(screen.getByTestId('order-card-27')).toHaveAttribute('data-highlighted', 'false') // opening the screen does not flag what is already there
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5100)
    })
    expect(await screen.findByTestId('order-card-28')).toHaveAttribute('data-highlighted', 'true')
    expect(board).toHaveBeenLastCalledWith('s1', { sinceRevision: 1, businessDate: '2026-10-05' })
    await waitFor(() => expect(document.title).toBe('(2) Заказы — Шаурма'))
  })

  it('shows «Нет связи» when polls keep failing for more than 30 seconds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    board.mockResolvedValueOnce(fullBoard()).mockRejectedValue({ message: 'Network Error' })
    renderScreen()
    await screen.findByTestId('order-card-27')
    expect(screen.queryByTestId('stale-banner')).toBeNull()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(36000)
    })
    expect(await screen.findByTestId('stale-banner')).toHaveTextContent('Нет связи, новые заказы могут не появиться')
    // the last known orders stay visible
    expect(screen.getByTestId('order-card-27')).toBeInTheDocument()
  })

  it('shows an error with a retry when the very first load fails', async () => {
    board.mockRejectedValue({ response: { status: 500, data: '' } })
    renderScreen()
    expect(await screen.findByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
