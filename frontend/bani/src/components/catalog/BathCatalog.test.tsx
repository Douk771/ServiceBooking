import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { BathResourceCardDto, BathsCatalogPageDto } from '../../types'
import { BathCatalog } from './BathCatalog'
import { BathSearchPanel } from './BathSearchPanel'

const api = vi.hoisted(() => ({ catalog: vi.fn(), cities: vi.fn() }))
vi.mock('../../api/bathsPublic', () => ({ bathsPublicApi: api }))

const card = (over: Partial<BathResourceCardDto> = {}): BathResourceCardDto => ({
  resourceId: 'r1',
  resourceSlug: 'chan',
  resourceName: 'Чан на дровах',
  companySlug: 'sever',
  companyName: 'Север',
  cityName: 'Шерегеш',
  priceFromRub: 2500,
  minHours: 2,
  capacity: 6,
  url: '/sever/chan',
  ...over,
})
const page = (over: Partial<BathsCatalogPageDto> = {}): BathsCatalogPageDto => ({
  items: [card()],
  totalCount: 1,
  page: 1,
  pageSize: 12,
  dateFilterApplied: false,
  ...over,
})
const httpError = (status: number, data: unknown = '') => Object.assign(new Error('http'), { response: { status, data } })

let search = ''
function Probe() {
  search = useLocation().search
  return null
}
function setup(url = '/') {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[url]}>
        <Probe />
        <BathSearchPanel />
        <BathCatalog />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  api.catalog.mockReset().mockResolvedValue(page())
  api.cities.mockReset().mockResolvedValue({ items: [{ id: 7, name: 'Шерегеш', region: null, resourcesCount: 1 }] })
})

describe('bani catalog', () => {
  it('shows the card with place, capacity and the price from', async () => {
    setup()
    expect(await screen.findByText('Чан на дровах')).toBeTruthy()
    expect(screen.getByText('Все бани: 1')).toBeTruthy()
    expect(screen.getByText(/до 6 человек/)).toBeTruthy()
    expect(screen.getByRole('link', { name: /Чан на дровах/ }).getAttribute('href')).toBe('/sever/chan')
    expect(api.catalog).toHaveBeenCalledWith({ page: 1, pageSize: 12 })
  })

  it('the filters come from the URL and go to the API; the card link carries the date', async () => {
    setup('/?city=7&date=2026-10-20')
    await screen.findByText('Чан на дровах')
    expect(api.catalog).toHaveBeenCalledWith({ cityId: 7, date: '2026-10-20', page: 1, pageSize: 12 })
    expect(screen.getByRole('link', { name: /Чан на дровах/ }).getAttribute('href')).toBe('/sever/chan?date=2026-10-20')
  })

  it('choosing a city writes the URL and resets the page', async () => {
    setup('/?page=3')
    await screen.findByText('Чан на дровах')
    await screen.findByRole('option', { name: 'Шерегеш' })
    await userEvent.selectOptions(screen.getByLabelText('Город'), '7')
    expect(search).toBe('?city=7')
  })

  it('empty list: the server text, and a reset when a filter is on', async () => {
    api.catalog.mockResolvedValue(page({ items: [], totalCount: 0, emptyText: 'На эту дату свободного времени нет' }))
    setup('/?date=2026-10-20')
    expect(await screen.findByText('На эту дату свободного времени нет')).toBeTruthy()
    await userEvent.click(screen.getAllByRole('button', { name: 'Сбросить фильтры' })[0])
    expect(search).toBe('')
  })

  it('error: the plain text of a 400 and a reset; a retry for other failures', async () => {
    api.catalog.mockRejectedValue(httpError(400, 'Город не найден'))
    setup('/?city=999')
    expect(await screen.findByText('Город не найден')).toBeTruthy()
    expect(screen.getAllByRole('button', { name: 'Сбросить фильтры' }).length).toBeGreaterThan(0)
  })

  it('error without a body offers a retry', async () => {
    api.catalog.mockRejectedValue(httpError(500))
    setup()
    expect(await screen.findByRole('button', { name: 'Повторить' }, { timeout: 4000 })).toBeTruthy()
  })

  it('a failed list of cities does not break the panel', async () => {
    api.cities.mockRejectedValue(httpError(500))
    setup()
    expect(await screen.findByText(/Список городов не загрузился/)).toBeTruthy()
    expect(screen.getByLabelText('Дата')).toBeTruthy()
  })
})
