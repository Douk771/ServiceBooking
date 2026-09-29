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
const orderingStatus = vi.fn()
const putAcceptance = vi.fn()
vi.mock('../../api/schedule', () => ({
  scheduleApi: { orderingStatus: (...a: unknown[]) => orderingStatus(...a), putAcceptance: (...a: unknown[]) => putAcceptance(...a) },
}))
vi.mock('../../api/catalog', () => ({ catalogApi: { products: () => Promise.resolve([]) } }))

const shop = {
  id: 's1', name: 'Шаурма', myRole: 'Staff', isActive: true,
  settings: { customerMode: 'Anyone', acceptanceMode: 'Manual', allowCustomerCancel: true, trackStock: false },
} as unknown as ShopManageDto

const ACCEPTING = { mode: 'Accepting', statusText: 'Принимаем заказы' } as const
const statusDto = (over: Record<string, unknown> = {}) => ({ acceptingOrders: true, openState: { isOpen: true, text: 'Открыто до 21:00' }, acceptance: ACCEPTING, scheduledAvailable: true, workingHoursSet: true, orderLimit: { used: 10, limit: 150, monthLabel: 'октябрь', warningLevel: 'None', text: null }, ...over })

function fullBoard(over: Partial<OrderBoardDto> = {}): OrderBoardDto {
  return { revision: 1, changed: true, businessDate: '2026-10-05', serverTimeUtc: '2026-10-05T10:05:00Z', acceptance: ACCEPTING, newOrders: [staffCard()], accepted: [], ready: [], completedToday: [], ...over }
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
  orderingStatus.mockReset()
  putAcceptance.mockReset()
  orderingStatus.mockResolvedValue(statusDto())
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
  describe('cycle 24: acceptance, limits, pick-up time', () => {
    it('pauses acceptance with the chosen duration and refreshes the board and the status', async () => {
      board.mockResolvedValue(fullBoard())
      putAcceptance.mockResolvedValue({ mode: 'Paused', statusText: 'Пауза до 13:30' })
      renderScreen()
      const user = userEvent.setup()
      const panel = await screen.findByTestId('acceptance-panel')
      expect(within(panel).getByText('Принимаем заказы')).toBeInTheDocument()
      await user.click(within(panel).getByRole('button', { name: '30 минут' }))
      expect(putAcceptance).toHaveBeenCalledWith('s1', { mode: 'Paused', pause: 'Minutes30' })
      await waitFor(() => expect(board.mock.calls.length).toBeGreaterThan(1))
    })

    it('offers only «Возобновить приём» while stopped, and shows the server reason for not accepting', async () => {
      board.mockResolvedValue(fullBoard({ acceptance: { mode: 'Stopped', statusText: 'Не принимаем, пока не включите' } }))
      orderingStatus.mockResolvedValue(statusDto({ acceptingOrders: false, ownerText: 'Приём заказов выключен', acceptance: { mode: 'Stopped', statusText: 'Не принимаем, пока не включите' } }))
      putAcceptance.mockResolvedValue(ACCEPTING)
      renderScreen()
      const user = userEvent.setup()
      const panel = await screen.findByTestId('acceptance-panel')
      expect(within(panel).queryByRole('button', { name: '15 минут' })).toBeNull()
      expect(await screen.findByText('Приём заказов выключен')).toBeInTheDocument()
      await user.click(within(panel).getByRole('button', { name: 'Возобновить приём' }))
      expect(putAcceptance).toHaveBeenCalledWith('s1', { mode: 'Accepting' })
    })

    it('shows the monthly-limit warning text from the server', async () => {
      board.mockResolvedValue(fullBoard())
      orderingStatus.mockResolvedValue(statusDto({ orderLimit: { used: 120, limit: 150, monthLabel: 'октябрь', warningLevel: 'Warning80', text: 'Заказов в этом месяце: 120 из 150' } }))
      renderScreen()
      const banner = await screen.findByTestId('limit-banner')
      expect(banner).toHaveTextContent('Заказов в этом месяце: 120 из 150')
      expect(banner).toHaveAttribute('data-level', 'Warning80')
    })

    it('shows a plain empty state instead of the banner when the status request fails, and the board still works', async () => {
      board.mockResolvedValue(fullBoard())
      orderingStatus.mockRejectedValue(new Error('boom'))
      renderScreen()
      expect(await screen.findByTestId('order-card-27')).toBeInTheDocument()
      expect(await screen.findByText(/Не удалось обновить статус приёма/)).toBeInTheDocument()
    })

    it('lists preorders by date under «Предзаказы» and marks an overdue order in words', async () => {
      const pre = staffCard({ id: 'p1', number: 5, status: 'Accepted', availableActions: ['MarkReady'], pickup: { kind: 'Slot', date: '2026-10-06', startUtc: '2026-10-06T09:00:00Z', dueUtc: '2026-10-06T09:00:00Z', text: 'Завтра, к 12:00', isPreorder: true, isOverdue: false } })
      const late = staffCard({ id: 'l1', number: 8, status: 'Accepted', availableActions: ['MarkReady'], pickup: { kind: 'Asap', date: '2026-10-05', startUtc: '2026-10-05T09:00:00Z', dueUtc: '2026-10-05T09:30:00Z', text: 'К 12:30', isPreorder: false, isOverdue: true } })
      board.mockResolvedValue(fullBoard({ newOrders: [], accepted: [late], preorders: [{ date: '2026-10-06', label: 'Завтра', orders: [pre] }] }))
      renderScreen()
      await screen.findByTestId('order-card-8')
      expect(within(screen.getByTestId('order-card-8')).getByText('Просрочен')).toBeInTheDocument()
      const pres = screen.getByTestId('preorders')
      expect(within(pres).getByText('Завтра')).toBeInTheDocument()
      expect(within(pres).getByTestId('order-card-5')).toHaveTextContent('Завтра, к 12:00')
      expect(within(column('Принятые')).queryByTestId('order-card-5')).toBeNull()
    })
  })
})
