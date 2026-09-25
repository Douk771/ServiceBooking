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

describe('CompanyPage — ARCHITECTURE_CYCLE13.md §204 (logo-over-gallery layer contract, R10, US-131)', () => {
  it('the row carrying the logo is `relative z-10`, so the fix survives the next redesign', async () => {
    getBySlug.mockResolvedValueOnce(makeCompany({ name: 'Гвоздь' }))
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)

    const heading = await screen.findByRole('heading', { name: 'Гвоздь' })
    // The row is the closest ancestor that also contains the logo/logo-placeholder — walk up from
    // the heading rather than hardcoding a DOM depth that a redesign could silently change.
    const row = heading.closest('div.relative.z-10')
    expect(row).not.toBeNull()
    expect(row?.className).toMatch(/\bflex\b/)
  })

  it.each([1, 3, 5, 10])('the logo placeholder renders on top with %i gallery photos', async (n) => {
    getBySlug.mockResolvedValueOnce(
      makeCompany({
        name: 'Гвоздь',
        photos: Array.from({ length: n }, (_, i) => ({
          id: `p${i}`,
          url: `/p${i}.jpg`,
          thumbnailUrl: `/p${i}-thumb.jpg`,
          width: 800,
          height: 600,
          position: i,
          isCover: i === 0,
        })),
      }),
    )
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)

    await screen.findByRole('heading', { name: 'Гвоздь' })
    // No logoUrl on this fixture company -> the placeholder icon renders; it must still exist (not
    // hidden behind the gallery) regardless of photo count.
    expect(document.querySelector('.-mt-\\[52px\\]')).not.toBeNull()
  })

  it('the lightbox overlay is a sibling of the carousel root, not nested inside its transformed track', async () => {
    const user = userEvent.setup()
    getBySlug.mockResolvedValueOnce(
      makeCompany({
        name: 'Гвоздь',
        photos: [
          { id: 'p0', url: '/p0.jpg', thumbnailUrl: '/p0-thumb.jpg', width: 800, height: 600, position: 0, isCover: true },
        ],
      }),
    )
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)
    await screen.findByRole('heading', { name: 'Гвоздь' })

    await user.click(screen.getAllByRole('img')[0])
    const closeButton = screen.getByRole('button', { name: 'Закрыть' })
    const carouselRoot = screen.getByRole('group', { name: 'Фотографии Гвоздь' })
    // The lightbox overlay must NOT be inside the carousel root (§204 — a `position: fixed`
    // descendant of a `transform`ed ancestor gets clipped by it instead of covering the viewport).
    expect(carouselRoot.contains(closeButton)).toBe(false)
  })
})

describe('CompanyMapLinks — ARCHITECTURE_CYCLE15.md §253/§285, wired into the address block', () => {
  it('shows a map link next to the address when the owner filled it in', async () => {
    getBySlug.mockResolvedValueOnce(
      makeCompany({ address: 'Ленина, 5', cityName: 'Барнаул', yandexMapsUrl: 'https://yandex.ru/maps/org/x/1/' }),
    )
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)

    expect(await screen.findByText('Ленина, 5')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Открыть в Яндекс Картах/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Открыть в 2ГИС/ })).not.toBeInTheDocument()
  })

  it('shows no map links when neither link is filled in, even with an address', async () => {
    getBySlug.mockResolvedValueOnce(makeCompany({ address: 'Ленина, 5', yandexMapsUrl: null, twoGisUrl: null }))
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)

    await screen.findByRole('heading', { name: makeCompany().name })
    expect(screen.queryByRole('link', { name: /Открыть в/ })).not.toBeInTheDocument()
  })
})

describe('phone link — ARCHITECTURE_CYCLE15.md §254', () => {
  it('renders the phone as a clickable tel: link, first in the contacts list', async () => {
    getBySlug.mockResolvedValueOnce(makeCompany({ phone: '79991234567', address: 'Ленина, 5' }))
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)

    const phoneLink = await screen.findByRole('link', { name: /Позвонить \+7/ })
    expect(phoneLink).toHaveAttribute('href', 'tel:79991234567')
  })

  it('renders no phone row when the company has none', async () => {
    getBySlug.mockResolvedValueOnce(makeCompany({ phone: undefined }))
    getForCompany.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })

    renderWithProviders(<CompanyPage />)

    await screen.findByRole('heading', { name: makeCompany().name })
    expect(screen.queryByRole('link', { name: /Позвонить/ })).not.toBeInTheDocument()
  })
})
