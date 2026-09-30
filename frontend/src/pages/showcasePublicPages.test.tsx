import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { HomePage } from './HomePage'
import { CompanyPage } from './CompanyPage'
import { EmbedPage } from './EmbedPage'
import { SHOWCASE_FALLBACK_TEXTS } from '../utils/showcaseTexts'
import type { Company } from '../types'

// Cycle 28 (API_CONTRACT_CYCLE28.md §591, §600): the «Пример» mark, the notice line and noindex on the three public
// pages. Component-level — every API module is mocked.

const getPublic = vi.fn()
const getBySlug = vi.fn()
const getText = vi.fn()

vi.mock('../api/companies', () => ({
  companiesApi: {
    getPublic: (...a: unknown[]) => getPublic(...a),
    getBySlug: (...a: unknown[]) => getBySlug(...a),
  },
}))
vi.mock('../api/cities', () => ({ citiesApi: { search: vi.fn().mockResolvedValue([]) } }))
vi.mock('../api/pricing', () => ({ pricingApi: { getPublicPricing: vi.fn().mockResolvedValue(null) } }))
vi.mock('../api/services', () => ({ servicesApi: { getByCompany: vi.fn().mockResolvedValue([]) } }))
vi.mock('../api/reviews', () => ({
  reviewsApi: { getForCompany: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 10, total: 0, hasNext: false }) },
}))
vi.mock('../api/legal', () => ({ legalApi: { getText: (...a: unknown[]) => getText(...a) } }))

const NOTICE = SHOWCASE_FALLBACK_TEXTS.ShowcaseNotice

function makeCompany(over: Partial<Company> = {}): Company {
  return {
    id: 'c1',
    name: 'Салон «Пример»',
    slug: 'primer-salon',
    allowSelfBooking: true,
    onlineBookingEnabled: true,
    ...over,
  }
}

function renderAt(path: string, routePath: string, ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path={routePath} element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const robots = () => document.head.querySelector('meta[name="robots"]')

beforeEach(() => {
  getPublic.mockReset()
  getBySlug.mockReset()
  getText.mockReset().mockRejectedValue({ response: { status: 404 } })
})

describe('HomePage catalog — showcase cards', () => {
  const page = (items: Company[]) => ({ items, page: 1, pageSize: 20, total: items.length, hasNext: false })

  it('marks a showcase card with a visible «Пример» and gives a screen reader the full notice', async () => {
    getPublic.mockResolvedValue(page([makeCompany({ isShowcase: true, showcaseBookingOpen: true })]))
    renderAt('/', '/', <HomePage />)

    const card = (await screen.findByRole('link', { name: /Салон «Пример»/ })) as HTMLElement
    expect(within(card).getByText('Пример')).toBeInTheDocument()
    // sr-only span carries the notice text (in the DOM, visually hidden by the utility class).
    expect(card).toHaveTextContent(NOTICE)
    expect(card.querySelector('.sr-only')).not.toBeNull()
  })

  it('an ordinary card has no «Пример»', async () => {
    getPublic.mockResolvedValue(page([makeCompany({ name: 'Барбершоп', slug: 'barber', isShowcase: false })]))
    renderAt('/', '/', <HomePage />)

    const card = await screen.findByRole('link', { name: /Барбершоп/ })
    expect(within(card).queryByText('Пример')).not.toBeInTheDocument()
  })

  it('«Онлайн-запись» is promised only where booking is actually accepted', async () => {
    getPublic.mockResolvedValue(
      page([
        makeCompany({ id: 'open', name: 'Открытая', slug: 'primer-open', isShowcase: true, showcaseBookingOpen: true }),
        makeCompany({ id: 'closed', name: 'Закрытая', slug: 'primer-closed', isShowcase: true, showcaseBookingOpen: false }),
        makeCompany({ id: 'real', name: 'Настоящая', slug: 'real' }),
      ]),
    )
    renderAt('/', '/', <HomePage />)

    const open = await screen.findByRole('link', { name: /Открытая/ })
    const closed = screen.getByRole('link', { name: /Закрытая/ })
    const real = screen.getByRole('link', { name: /Настоящая/ })
    expect(within(open).getByText('Онлайн-запись')).toBeInTheDocument()
    expect(within(closed).queryByText('Онлайн-запись')).not.toBeInTheDocument()
    expect(within(real).getByText('Онлайн-запись')).toBeInTheDocument()
  })
})

describe('CompanyPage — showcase company', () => {
  it('shows the badge in the card, the notice under it, and noindex while open; noindex leaves with the page', async () => {
    getBySlug.mockResolvedValue(makeCompany({ isShowcase: true, showcaseBookingOpen: true }))
    const { unmount } = renderAt('/company/primer-salon', '/company/:slug', <CompanyPage />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Салон «Пример»' })).toBeInTheDocument()
    expect(screen.getByText('Пример')).toBeInTheDocument()
    expect(screen.getByTestId('showcase-notice')).toHaveTextContent(NOTICE)
    await waitFor(() => expect(robots()?.getAttribute('content')).toBe('noindex, nofollow'))

    unmount()
    expect(robots()).toBeNull()
  })

  it('a live ShowcaseNotice from legal.json replaces the fallback', async () => {
    getText.mockImplementation((key: string) =>
      key === 'ShowcaseNotice'
        ? Promise.resolve({ key, version: '1', isDraft: false, contentHtml: '<h2>Текст</h2><p>Опубликованный текст.</p>' })
        : Promise.reject({ response: { status: 404 } }),
    )
    getBySlug.mockResolvedValue(makeCompany({ isShowcase: true }))
    renderAt('/company/primer-salon', '/company/:slug', <CompanyPage />)

    await waitFor(() => expect(screen.getByTestId('showcase-notice')).toHaveTextContent('Опубликованный текст.'))
    expect(screen.getByTestId('showcase-notice')).not.toHaveTextContent('вымышленная')
  })

  it('an ordinary company: no badge, no notice, no noindex, no request for the showcase text', async () => {
    getBySlug.mockResolvedValue(makeCompany({ name: 'Барбершоп', slug: 'gvozd', isShowcase: false }))
    renderAt('/company/gvozd', '/company/:slug', <CompanyPage />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Барбершоп' })).toBeInTheDocument()
    expect(screen.queryByText('Пример')).not.toBeInTheDocument()
    expect(screen.queryByTestId('showcase-notice')).not.toBeInTheDocument()
    expect(robots()).toBeNull()
    expect(getText).not.toHaveBeenCalledWith('ShowcaseNotice')
  })

  it('a closed showcase company still renders its booking UI (the refusal is the server\'s, §591)', async () => {
    getBySlug.mockResolvedValue(makeCompany({ isShowcase: true, showcaseBookingOpen: false }))
    renderAt('/company/primer-salon', '/company/:slug', <CompanyPage />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Салон «Пример»' })).toBeInTheDocument()
    expect(screen.queryByText(/Онлайн-запись в этой компании сейчас недоступна/)).not.toBeInTheDocument()
  })
})

describe('EmbedPage — showcase company', () => {
  it('shows the badge and the notice and adds noindex', async () => {
    getBySlug.mockResolvedValue(makeCompany({ isShowcase: true, showcaseBookingOpen: true }))
    renderAt('/embed/primer-salon', '/embed/:slug', <EmbedPage />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Салон «Пример»' })).toBeInTheDocument()
    expect(screen.getByText('Пример')).toBeInTheDocument()
    expect(screen.getByTestId('showcase-notice')).toHaveTextContent(NOTICE)
    await waitFor(() => expect(robots()?.getAttribute('content')).toBe('noindex, nofollow'))
  })

  it('an ordinary company embed is untouched', async () => {
    getBySlug.mockResolvedValue(makeCompany({ name: 'Барбершоп', slug: 'gvozd' }))
    renderAt('/embed/gvozd', '/embed/:slug', <EmbedPage />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Барбершоп' })).toBeInTheDocument()
    expect(screen.queryByTestId('showcase-notice')).not.toBeInTheDocument()
    expect(robots()).toBeNull()
  })
})
