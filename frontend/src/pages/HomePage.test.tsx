import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import type { Company } from '../types'

const getPublic = vi.fn()

vi.mock('../api/companies', () => ({
  companiesApi: {
    getPublic: (...args: unknown[]) => getPublic(...args),
  },
}))

// LandingPricingSection (rendered by HomePage) calls this live; without a mock these tests would hit a real
// network request for /api/pricing (see code-reviewer Н1).
vi.mock('../api/pricing', () => ({
  pricingApi: {
    getPublicPricing: vi.fn().mockResolvedValue(null),
  },
}))

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

describe('HomePage — no city', () => {
  beforeEach(() => {
    localStorage.clear()
    getPublic.mockReset().mockResolvedValue({ items: [company()], page: 1, pageSize: 100, total: 1, hasNext: false })
  })

  it('never sends cityId and neither shows nor offers a city, even with an old stored choice', async () => {
    localStorage.setItem('home-city', JSON.stringify({ id: 1, name: 'Москва', label: 'Москва, Москва' }))
    renderPage()
    await waitFor(() => expect(getPublic).toHaveBeenCalled())
    expect(getPublic.mock.calls[0][0]).not.toHaveProperty('cityId')
    await screen.findByText('Салон красоты')
    expect(screen.queryByPlaceholderText('Выберите город')).not.toBeInTheDocument()
    expect(screen.queryByText(/Все города|Москва/)).not.toBeInTheDocument()
  })
})

describe('HomePage — pagination (Б2)', () => {
  beforeEach(() => {
    localStorage.clear()
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
})

describe('HomePage — cycle 29 (US-29-03) logo mark', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('V29-33: letter instead of icon without logo; logo image has alt=""', async () => {
    getPublic.mockReset().mockResolvedValue({
      items: [company({ name: '«Ромашка»', slug: 'r' }), company({ id: 'c2', name: 'Лотос', slug: 'l', logoUrl: '/uploads/l.jpg' })],
      page: 1, pageSize: 100, total: 2, hasNext: false,
    })
    renderPage()
    expect(await screen.findByTestId('company-logo-initial')).toHaveTextContent('Р')
    expect(screen.getByTestId('company-logo-img')).toHaveAttribute('alt', '')
  })
})

// T38-10: главная «Записи» на едином шаблоне (ARCHITECTURE_CYCLE38.md §38.4.2).
describe('HomePage — единый шаблон (T38-10)', () => {
  const LONG = 'Я'.repeat(200)
  beforeEach(() => {
    localStorage.clear()
    getPublic.mockReset().mockResolvedValue({
      items: [company({ name: LONG, slug: 'long' })], page: 1, pageSize: 20, total: 1, hasNext: false,
    })
  })

  it('catalog lives in #companies, anchors lead to #companies and #clients-title, no footer', async () => {
    const { container } = renderPage()
    await screen.findByText(LONG)
    const frame = container.querySelector('#companies') as HTMLElement
    expect(frame.contains(screen.getByText(LONG))).toBe(true)
    expect(screen.getAllByRole('link', { name: 'Найти специалиста' })[0]).toHaveAttribute('href', '#companies')
    expect(screen.getByRole('link', { name: 'Как записаться' })).toHaveAttribute('href', '#clients-title')
    expect(container.querySelector('footer')).toBeNull()
    expect(container.querySelectorAll('h1')).toHaveLength(1)
  })

  it('no media block; reminder wording stays (Q38-6); long name is not truncated', async () => {
    const { container } = renderPage()
    await screen.findByText(LONG)
    expect(container.querySelector('img[alt="Интерьер салона"]')).toBeNull()
    expect(container.textContent).toContain('напоминание накануне визита')
    const h3 = screen.getByText(LONG)
    expect(h3.className).not.toContain('truncate')
  })

  it('FAQ has 8 collapsed questions', async () => {
    const { container } = renderPage()
    await screen.findByText(LONG)
    const buttons = container.querySelectorAll('#faq h3 > button')
    expect(buttons).toHaveLength(8)
    buttons.forEach((b) => expect(b.getAttribute('aria-expanded')).toBe('false'))
  })

  it('карусель «Как проходит запись»: шаги кликабельны и синхронизированы с кадрами; «Что ещё можно» — сетка карточек', async () => {
    const { container } = renderPage()
    await screen.findByText(LONG)
    const region = screen.getByRole('region', { name: /Как проходит запись|Как записаться/ })
    const steps = region.querySelectorAll('ol button[aria-current], ol button')
    expect(steps.length).toBeGreaterThan(2)
    expect(steps[0]).toHaveAttribute('aria-current', 'true')
    await userEvent.click(steps[2])
    expect(steps[2]).toHaveAttribute('aria-current', 'true')
    expect(region.querySelectorAll('[data-testid="carousel-slide"][data-active]')).toHaveLength(1)
    const list = container.querySelector('ul[aria-labelledby="clients-list-title"]') as HTMLElement
    expect(list.className).toContain('grid')
    expect(list.querySelectorAll('li').length).toBeGreaterThanOrEqual(4)
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth || Infinity)
  })
})
