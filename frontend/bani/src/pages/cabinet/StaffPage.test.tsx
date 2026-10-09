import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StaffPage } from './StaffPage'

/** A failed request as axios reports it: a real Error that carries the response. */
const httpError = (status: number, data: unknown = '') => Object.assign(new Error('http'), { response: { status, data } })

const list = vi.fn()
vi.mock('../../api/bathsMembers', () => ({ bathsMembersApi: { list: (...a: unknown[]) => list(...a), add: vi.fn(), setPosition: vi.fn(), remove: vi.fn() } }))
let perms: string[] = ['ManageCompany']
vi.mock('../../cabinet/cabinetVertical', () => ({ useBathsCompany: () => ({ company: { id: 'c1', myPermissions: perms }, refresh: () => undefined }) }))

const renderPage = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <StaffPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )

describe('StaffPage', () => {
  beforeEach(() => {
    list.mockReset()
    perms = ['ManageCompany']
  })

  it('names the positions «Администратор» and «Банщик», never the words of houses', async () => {
    list.mockResolvedValue([
      { id: 'm0', userId: 'u0', firstName: 'Олег', lastName: 'Баня', phone: '79001112233', role: 'CompanyOwner', position: null },
      { id: 'm1', userId: 'u1', firstName: 'Ира', lastName: 'Мир', phone: '79004445566', role: 'Master', position: 'Manager' },
      { id: 'm2', userId: 'u2', firstName: 'Паша', lastName: 'Пар', phone: '79007778899', role: 'Master', position: 'Housekeeper' },
    ])
    renderPage()
    expect(await screen.findByText(/Владелец/)).toBeInTheDocument()
    const team = screen.getByRole('region', { name: 'Сотрудники компании' })
    expect(within(team).getByLabelText('Должность: Ира Мир')).toHaveDisplayValue('Администратор')
    expect(within(team).getByLabelText('Должность: Паша Пар')).toHaveDisplayValue('Банщик')
    expect(document.body.textContent).not.toMatch(/Управляющий|Горничная/)
  })

  it('offers the two positions in the add form', async () => {
    list.mockResolvedValue([])
    renderPage()
    const select = await screen.findByLabelText('Должность')
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['Администратор', 'Банщик'])
  })

  it('shows the empty state and the error state with a retry', async () => {
    list.mockResolvedValueOnce([])
    const { unmount } = renderPage()
    expect(await screen.findByText('Сотрудников пока нет')).toBeInTheDocument()
    unmount()
    list.mockRejectedValue(httpError(500))
    renderPage()
    expect(await screen.findByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })

  it('does not open to someone without the right to manage the company', () => {
    perms = ['ViewCabinet']
    list.mockResolvedValue([])
    renderPage()
    expect(screen.getByText('Раздел недоступен')).toBeInTheDocument()
  })
})
