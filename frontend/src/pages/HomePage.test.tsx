import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import type { City, Company } from '../types'

const getPublic = vi.fn()
const citiesSearch = vi.fn()

vi.mock('../api/companies', () => ({
  companiesApi: {
    getPublic: (...args: unknown[]) => getPublic(...args),
  },
}))

vi.mock('../api/cities', () => ({
  citiesApi: {
    search: (...args: unknown[]) => citiesSearch(...args),
  },
}))

// PricingTeaser (rendered by HomePage) calls this live; without a mock these tests would hit a real
// network request for /api/pricing (see code-reviewer Н1).
vi.mock('../api/pricing', () => ({
  pricingApi: {
    getPublicPricing: vi.fn().mockResolvedValue(null),
  },
}))

const MOSCOW: City = {
  id: 1,
  name: 'Москва',
  region: 'Москва',
  timeZoneId: 'Europe/Moscow',
  utcOffsetMinutes: 180,
  label: 'Москва, Москва',
}

function company(overrides: Partial<Company> = {}): Company {
  return {
    id: 'c1',
    name: 'Салон красоты',
    slug: 'salon',
    allowSelfBooking: true,
    ...overrides,
  }
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/**
 * US-115 (SPEC.md): city filtering on the homepage must be a server-side query parameter, never a
 * client-side filter over a fully-downloaded list (SPEC §6/§9.17 regression class).
 */
describe('HomePage — US-115 city selection', () => {
  beforeEach(() => {
    localStorage.clear()
    getPublic.mockReset().mockResolvedValue({ items: [company()], page: 1, pageSize: 100, total: 1, hasNext: false })
    citiesSearch.mockReset().mockResolvedValue([MOSCOW])
  })

  it('fetches without cityId when no city is selected ("Все города")', async () => {
    renderPage()
    await waitFor(() => expect(getPublic).toHaveBeenCalled())
    const params = getPublic.mock.calls[0][0]
    expect(params.cityId).toBeUndefined()
  })

  it('restores a previously selected city from localStorage and requests it by cityId', async () => {
    localStorage.setItem('home-city', JSON.stringify(MOSCOW))
    renderPage()
    await waitFor(() => expect(getPublic).toHaveBeenCalled())
    const params = getPublic.mock.calls[getPublic.mock.calls.length - 1][0]
    expect(params.cityId).toBe(MOSCOW.id)
    expect(await screen.findByDisplayValue(MOSCOW.label)).toBeInTheDocument()
  })

  it('selecting a city persists it to localStorage; clicking "Все города" clears both', async () => {
    const user = userEvent.setup()
    renderPage()
    await waitFor(() => expect(getPublic).toHaveBeenCalled())

    await user.click(screen.getByPlaceholderText('Выберите город'))
    await waitFor(() => expect(citiesSearch).toHaveBeenCalled())
    await user.click(await screen.findByRole('option', { name: /Москва/ }))

    await waitFor(() => expect(localStorage.getItem('home-city')).toContain('Москва'))
    await waitFor(() => {
      const last = getPublic.mock.calls[getPublic.mock.calls.length - 1][0]
      expect(last.cityId).toBe(MOSCOW.id)
    })

    const clearButton = await screen.findByRole('button', { name: 'Все города' })
    await user.click(clearButton)

    await waitFor(() => expect(localStorage.getItem('home-city')).toBeNull())
    await waitFor(() => {
      const last = getPublic.mock.calls[getPublic.mock.calls.length - 1][0]
      expect(last.cityId).toBeUndefined()
    })
  })

  it('shows a city-specific empty state with a way back to "Все города" when the chosen city has no companies', async () => {
    localStorage.setItem('home-city', JSON.stringify(MOSCOW))
    getPublic.mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0, hasNext: false })
    renderPage()

    expect(await screen.findByText(/пока нет компаний/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Показать все города' })).toBeInTheDocument()
  })

  it('ignores a malformed stored city (e.g. "{}") and falls back to "Все города"', async () => {
    localStorage.setItem('home-city', JSON.stringify({}))
    renderPage()
    await waitFor(() => expect(getPublic).toHaveBeenCalled())
    const params = getPublic.mock.calls[0][0]
    expect(params.cityId).toBeUndefined()
    expect(localStorage.getItem('home-city')).toBeNull()
  })
})

describe('HomePage — pagination (Б2)', () => {
  beforeEach(() => {
    localStorage.clear()
    citiesSearch.mockReset().mockResolvedValue([MOSCOW])
  })

  it('requests page 1 with the shared page size and passes page/hasNext to Pagination', async () => {
    getPublic.mockReset().mockResolvedValue({
      items: [company()],
      page: 1,
      pageSize: 20,
      total: 45,
      hasNext: true,
    })
    renderPage()

    await waitFor(() => expect(getPublic).toHaveBeenCalled())
    const params = getPublic.mock.calls[0][0]
    expect(params.page).toBe(1)
    expect(params.pageSize).toBe(20)

    expect(await screen.findByRole('button', { name: 'Следующая страница' })).toBeEnabled()
  })

  it('advancing to the next page requests page 2', async () => {
    const user = userEvent.setup()
    getPublic.mockReset().mockResolvedValue({
      items: [company()],
      page: 1,
      pageSize: 20,
      total: 45,
      hasNext: true,
    })
    renderPage()

    const nextButton = await screen.findByRole('button', { name: 'Следующая страница' })
    await user.click(nextButton)

    await waitFor(() => {
      const last = getPublic.mock.calls[getPublic.mock.calls.length - 1][0]
      expect(last.page).toBe(2)
    })
  })

  it('resets to page 1 when the city filter changes', async () => {
    const user = userEvent.setup()
    getPublic.mockReset().mockResolvedValue({
      items: [company()],
      page: 1,
      pageSize: 20,
      total: 45,
      hasNext: true,
    })
    renderPage()

    const nextButton = await screen.findByRole('button', { name: 'Следующая страница' })
    await user.click(nextButton)
    await waitFor(() => {
      const last = getPublic.mock.calls[getPublic.mock.calls.length - 1][0]
      expect(last.page).toBe(2)
    })

    await user.click(screen.getByPlaceholderText('Выберите город'))
    await waitFor(() => expect(citiesSearch).toHaveBeenCalled())
    await user.click(await screen.findByRole('option', { name: /Москва/ }))

    await waitFor(() => {
      const last = getPublic.mock.calls[getPublic.mock.calls.length - 1][0]
      expect(last.cityId).toBe(MOSCOW.id)
      expect(last.page).toBe(1)
    })
  })
})
