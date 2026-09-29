import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { MenuPage } from './MenuPage'
import type { DailyMenuDto, ShopManageDto } from '../../types'

const calendar = vi.fn()
const get = vi.fn()
const put = vi.fn()
const remove = vi.fn()
vi.mock('../../api/menu', () => ({
  menuApi: { calendar: (...a: unknown[]) => calendar(...a), get: (...a: unknown[]) => get(...a), put: (...a: unknown[]) => put(...a), remove: (...a: unknown[]) => remove(...a), copy: vi.fn() },
}))

const menu = (over: Partial<DailyMenuDto> = {}): DailyMenuDto => ({
  date: '2026-10-01', label: 'Завтра', exists: false, productIds: ['a'],
  products: [
    { productId: 'a', name: 'Борщ', categoryName: 'Горячее', isPublished: true, inMenu: true, allowedByWeekdays: true },
    { productId: 'b', name: 'Компот', categoryName: 'Напитки', isPublished: true, inMenu: false, allowedByWeekdays: false },
  ],
  ...over,
})

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/s1/menu']}>
        <Routes>
          <Route element={<Outlet context={{ shop: { id: 's1', name: 'Шаурма' } as ShopManageDto, isOwner: false }} />}>
            <Route path="/cabinet/:shopId/menu" element={<MenuPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  calendar.mockReset().mockResolvedValue({ from: '2026-10-01', to: '2026-10-08', days: [{ date: '2026-10-01', label: 'Завтра', hasMenu: false, productCount: 0 }, { date: '2026-10-02', label: 'пт 2 окт', hasMenu: true, productCount: 3 }] })
  get.mockReset().mockResolvedValue(menu())
  put.mockReset()
  remove.mockReset()
})

describe('MenuPage', () => {
  it('shows the calendar and a date pre-filled by the server, saying it is not saved yet', async () => {
    renderPage()
    expect(await screen.findByRole('button', { name: /пт 2 окт/ })).toHaveTextContent('меню: 3')
    expect(await screen.findByTestId('menu-state')).toHaveTextContent('Меню ещё не сохранено')
    expect(screen.getByRole('checkbox', { name: /Борщ/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /Компот/ })).not.toBeChecked()
    expect(screen.getByText('не по дню недели')).toBeInTheDocument()
  })

  it('saves exactly the ticked products and then shows the saved state with «Удалить меню»', async () => {
    put.mockResolvedValue(menu({ exists: true, productIds: ['a', 'b'], products: menu().products.map((p) => ({ ...p, inMenu: true })) }))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('checkbox', { name: /Компот/ }))
    await user.click(screen.getByRole('button', { name: 'Сохранить меню' }))
    await waitFor(() => expect(put).toHaveBeenCalledWith('s1', '2026-10-01', ['a', 'b']))
    expect(await screen.findByText('Меню на эту дату сохранено.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Удалить меню' })).toBeInTheDocument()
  })

  it('deletes the menu after a confirmation that says orders stay untouched', async () => {
    get.mockResolvedValue(menu({ exists: true }))
    remove.mockResolvedValue(undefined)
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Удалить меню' }))
    expect(screen.getByText(/Уже созданные заказы не изменятся/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Удалить' }))
    await waitFor(() => expect(remove).toHaveBeenCalledWith('s1', '2026-10-01'))
  })

  it('shows the server 400 text and an error state with a retry', async () => {
    put.mockImplementation(() => Promise.reject({ response: { status: 400, data: 'Товар не найден' } }))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('checkbox', { name: /Компот/ }))
    await user.click(screen.getByRole('button', { name: 'Сохранить меню' }))
    expect(await screen.findByText('Товар не найден')).toBeInTheDocument()
  })

  it('has an error state for the calendar and an empty state without dates', async () => {
    calendar.mockImplementation(() => Promise.reject({ response: { status: 500, data: '' } }))
    const first = renderPage()
    expect(await screen.findByRole('button', { name: 'Повторить' })).toBeInTheDocument()
    first.unmount()
    calendar.mockResolvedValue({ from: '', to: '', days: [] })
    renderPage()
    expect(await screen.findByText('Нет дат для меню')).toBeInTheDocument()
  })
})
