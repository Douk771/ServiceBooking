import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { HousesPage } from './HousesPage'
import { companyFixture, httpError } from '../../test/fixtures'
import type { HouseListItemDto, StaysCompanyManageDto } from '../../types'

const api = vi.hoisted(() => ({ list: vi.fn(), order: vi.fn() }))
vi.mock('../../api/staysHouses', () => ({ staysHousesApi: api }))

function house(id: string, over: Partial<HouseListItemDto> = {}): HouseListItemDto {
  return { id, slug: id, name: `Дом ${id}`, coverUrl: null, capacity: 4, isPublished: false, isArchived: false, priceMode: 'Constant', priceFromRub: null, position: 0, publicUrl: `https://dom/${id}`, publishProblems: [], ...over }
}

function renderPage(company: StaysCompanyManageDto) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/co-1/houses']}>
        <Routes>
          <Route element={<Outlet context={{ company, refresh: () => undefined }} />}>
            <Route path="/cabinet/:companyId/houses" element={<HousesPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => Object.values(api).forEach((f) => f.mockReset()))

describe('HousesPage', () => {
  it('empty company: invites to add the first house', async () => {
    api.list.mockResolvedValue([])
    renderPage(companyFixture())
    expect(await screen.findByText('Домов пока нет')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Добавить дом/ }).length).toBeGreaterThan(0)
  })

  it('shows the state of each house and what stops its publication; archived ones go to the archive', async () => {
    api.list.mockResolvedValue([
      house('a', { isPublished: true, priceFromRub: 5000 }),
      house('b', { publishProblems: ['NoPrice'] }),
      house('c', { isArchived: true }),
    ])
    renderPage(companyFixture())
    expect(await screen.findByText('Опубликован')).toBeInTheDocument()
    expect(screen.getByText(/Задайте цену/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Архив' })).toBeInTheDocument()
    expect(screen.getByText('В архиве')).toBeInTheDocument()
  })

  it('moving a house sends the FULL order of the non-archived houses', async () => {
    api.list.mockResolvedValue([house('a'), house('b'), house('c', { isArchived: true })])
    api.order.mockResolvedValue([house('b'), house('a'), house('c', { isArchived: true })])
    renderPage(companyFixture())
    fireEvent.click(await screen.findByRole('button', { name: 'Опустить ниже: Дом a' }))
    await waitFor(() => expect(api.order).toHaveBeenCalledWith('co-1', ['b', 'a']))
  })

  it('a manager (no ManageHouses) sees the list but no add button and no order arrows', async () => {
    api.list.mockResolvedValue([house('a'), house('b')])
    renderPage(companyFixture({ myRole: 'Manager', myPermissions: ['EditHouseContent', 'ViewBookings', 'ViewCabinet'] }))
    await screen.findByText('Дом a')
    expect(screen.queryByRole('link', { name: /Добавить дом/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Опустить ниже/ })).not.toBeInTheDocument()
  })

  it('a failed load offers a retry', async () => {
    api.list.mockRejectedValueOnce(httpError(500, '')).mockResolvedValueOnce([house('a')])
    renderPage(companyFixture())
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервер временно недоступен')
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(await screen.findByText('Дом a')).toBeInTheDocument()
  })
})
