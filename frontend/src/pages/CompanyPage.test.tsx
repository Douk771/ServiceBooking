import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CompanyPage } from './CompanyPage'
import type { Company, Service } from '../types'
import type { Review } from '../api/reviews'

const getBySlug = vi.fn()
const getByCompany = vi.fn()
const getForCompany = vi.fn()

vi.mock('../api/companies', () => ({
  companiesApi: {
    getBySlug: (...args: unknown[]) => getBySlug(...args),
  },
}))

vi.mock('../api/services', () => ({
  servicesApi: {
    getByCompany: (...args: unknown[]) => getByCompany(...args),
  },
}))

vi.mock('../api/reviews', () => ({
  reviewsApi: {
    getForCompany: (...args: unknown[]) => getForCompany(...args),
  },
}))

beforeEach(() => {
  getBySlug.mockReset()
  getByCompany.mockReset().mockResolvedValue([] as Service[])
  getForCompany.mockReset()
})

function renderWithProviders(ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/company/gvozd']}>
        <Routes>
          <Route path="/company/:slug" element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

function makeCompany(overrides: Partial<Company> = {}): Company {
  return {
    id: 'co1',
    name: 'Барбершоп «Гвоздь»',
    slug: 'gvozd',
    allowSelfBooking: true,
    ...overrides,
  }
}

function makeReview(overrides: Partial<Review> = {}): Review {
  return {
    rating: 5,
    comment: 'Отлично',
    reviewerName: 'Иван',
    masterName: 'Пётр',
    serviceName: 'Стрижка',
    createdAt: '2026-09-01T09:00:00Z',
    ...overrides,
  }
}

describe('CompanyPage — review rating', () => {
  it('shows the server-computed aggregate rating and total, not derived from the current page', async () => {
    getBySlug.mockResolvedValueOnce(makeCompany({ averageRating: 4.2, reviewCount: 137 }))
    // The current review PAGE has a very different average (all 1-star) — if the UI recomputed from
    // this it would show 1.0, not the server aggregate 4.2. This is exactly the regression this test
    // guards against.
    getForCompany.mockResolvedValueOnce({
      items: [makeReview({ rating: 1 }), makeReview({ rating: 1 })],
      page: 1,
      pageSize: 20,
      total: 137,
      hasNext: true,
    })

    renderWithProviders(<CompanyPage />)

    expect(await screen.findByText('4.2')).toBeInTheDocument()
    expect(screen.getByText('· 137 отзывов')).toBeInTheDocument()
  })

  it('does not show a rating badge when the company has no aggregate rating yet', async () => {
    getBySlug.mockResolvedValueOnce(makeCompany({ averageRating: null, reviewCount: 0 }))
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)

    await screen.findByText('Пока нет отзывов')
    expect(screen.queryByText(/^·/)).not.toBeInTheDocument()
  })

  it('keeps showing the same aggregate rating after paging to a second page of reviews', async () => {
    const user = userEvent.setup()
    getBySlug.mockResolvedValueOnce(makeCompany({ averageRating: 4.2, reviewCount: 137 }))
    getForCompany.mockResolvedValueOnce({
      items: [makeReview({ rating: 5 })],
      page: 1,
      pageSize: 20,
      total: 137,
      hasNext: true,
    })

    renderWithProviders(<CompanyPage />)
    expect(await screen.findByText('4.2')).toBeInTheDocument()
    // Wait for page 1 of reviews to actually land before queuing page 2's response, otherwise the
    // pagination button below (which depends on `hasNext` from the reviews query) may not exist yet.
    await screen.findByRole('button', { name: 'Следующая страница' })

    // Page 2 comes back with a completely different set of ratings — if the badge were still derived
    // from `reviewsData.items` (the regression this guards against) it would change here.
    getForCompany.mockResolvedValueOnce({
      items: [makeReview({ rating: 2 }), makeReview({ rating: 3 })],
      page: 2,
      pageSize: 20,
      total: 137,
      hasNext: false,
    })

    await user.click(screen.getByRole('button', { name: 'Следующая страница' }))
    await waitFor(() => expect(getForCompany).toHaveBeenCalledTimes(2))

    expect(screen.getByText('· 137 отзывов')).toBeInTheDocument()
    expect(screen.getByText('4.2')).toBeInTheDocument()
  })
})
