import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import type { Company } from '../types'
import { useAuthStore } from '../store/authStore'
import contract from '../../../contracts/cycle46/zapis-landing.json'
import { fillOperatorTemplate } from '../utils/operatorRequisites'

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

  it('catalog lives in #companies, the client link leads to #companies, no how-to link, no footer', async () => {
    const { container } = renderPage()
    await screen.findByText(LONG)
    const frame = container.querySelector('#companies') as HTMLElement
    expect(frame.contains(screen.getByText(LONG))).toBe(true)
    expect(screen.getByRole('link', { name: 'Салоны на EZBOOK' })).toHaveAttribute('href', '#companies')
    expect(screen.queryByRole('link', { name: 'Как записываются клиенты' })).toBeNull()
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

  it('FAQ has 8 collapsed questions (CY46-05)', async () => {
    const { container } = renderPage()
    await screen.findByText(LONG)
    const buttons = container.querySelectorAll('#faq h3 > button')
    expect(buttons).toHaveLength(8)
    buttons.forEach((b) => expect(b.getAttribute('aria-expanded')).toBe('false'))
  })

  it('карусель «Шаг за шагом»: шаги кликабельны и синхронизированы с кадрами; «Что ещё может клиент» — плотный список без карточек', async () => {
    const { container } = renderPage()
    await screen.findByText(LONG)
    const region = screen.getByRole('region', { name: 'Шаг за шагом' })
    const steps = region.querySelectorAll('ol button[aria-current], ol button')
    expect(steps.length).toBeGreaterThan(2)
    expect(steps[0]).toHaveAttribute('aria-current', 'true')
    await userEvent.click(steps[2])
    expect(steps[2]).toHaveAttribute('aria-current', 'true')
    expect(region.querySelectorAll('[data-testid="carousel-slide"][data-active]')).toHaveLength(1)
    const list = container.querySelector('ul[aria-labelledby="clients-list-title"]') as HTMLElement
    expect(list.className).toContain('grid')
    expect(list.querySelectorAll('li')).toHaveLength(5)
    list.querySelectorAll('li').forEach((li) => expect(li.className).not.toContain('border'))
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth || Infinity)
  })
})

// CY46: главная «Записи» под владельца салона (ARCHITECTURE_CYCLE46_ZAPIS_LANDING.md §46.11.1).
describe('HomePage — раскладка business-first (CY46)', () => {
  beforeEach(() => {
    localStorage.clear()
    useAuthStore.setState({ user: null, token: null })
    getPublic.mockReset().mockResolvedValue({ items: [company()], page: 1, pageSize: 20, total: 1, hasNext: false })
  })

  it('CY46-01: порядок h1, #biz-title, #clients-title, #biz-steps-title, #faq-title, #companies; все — h2; каталог последний', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const seq = ['h1', '#biz-title', '#clients-title', '#biz-steps-title', '#faq-title', '#companies'].map(
      (sel) => container.querySelector(sel) as HTMLElement,
    )
    seq.forEach((el) => expect(el).not.toBeNull())
    for (let i = 1; i < seq.length; i++) {
      expect(seq[i - 1].compareDocumentPosition(seq[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
    for (const sel of ['#biz-title', '#clients-title', '#biz-steps-title', '#faq-title']) {
      expect(container.querySelector(sel)!.tagName).toBe('H2')
    }
    const sections = container.querySelectorAll('main > section')
    expect(sections[sections.length - 1].id).toBe('companies')
  })

  it('CY46-01: в секции клиентов нет ссылок и панели шагов; в бизнес-секции нет ol; шаги — h2 → h3', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const clients = container.querySelector('[aria-labelledby="clients-title"]') as HTMLElement
    expect(clients.querySelector('a')).toBeNull()
    expect(container.querySelector('#clients-steps-title')).toBeNull()
    expect((container.querySelector('[aria-labelledby="biz-title"]') as HTMLElement).querySelector('ol')).toBeNull()
    const steps = container.querySelector('[aria-labelledby="biz-steps-title"]') as HTMLElement
    expect(steps.querySelectorAll('h3')).toHaveLength(3)
    expect(steps.querySelectorAll('h4')).toHaveLength(0)
  })

  it('CY46-04: «Подключить салон» в первом экране — гость на /register, вошедший на /cabinet/new', async () => {
    const { container, unmount } = renderPage()
    await screen.findByText('Салон красоты')
    const first = container.querySelector('main > div') as HTMLElement
    const guestLink = first.querySelector('a[href^="/register"]') as HTMLElement
    expect(guestLink).toHaveTextContent('Подключить салон')
    expect(guestLink).toHaveAttribute('href', '/register?returnTo=%2Fcabinet%2Fnew')
    unmount()
    useAuthStore.setState({ user: { id: 'u', phone: '7', firstName: 'a', lastName: 'b', roles: ['Client'] }, token: 't' })
    const again = renderPage()
    await screen.findByText('Салон красоты')
    expect((again.container.querySelector('main > div') as HTMLElement).querySelector('a[href="/cabinet/new"]')).toHaveTextContent('Подключить салон')
  })

  it('CY46-03: переход с #biz-title прокручивает к бизнес-секции', async () => {
    // jsdom не реализует scrollIntoView: подменяем и возвращаем как было, hash сбрасываем даже при упавшем assert.
    const original = Element.prototype.scrollIntoView
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    window.location.hash = '#biz-title'
    try {
      const { container } = renderPage()
      await screen.findByText('Салон красоты')
      expect(scrollIntoView).toHaveBeenCalledTimes(1)
      expect(scrollIntoView.mock.instances[0]).toBe(container.querySelector('#biz-title'))
    } finally {
      window.location.hash = ''
      Element.prototype.scrollIntoView = original
    }
  })

  it('CY46-08: каждая строка контракта (кроме FAQ-ответов в скрытых узлах) видна на странице', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const text = container.textContent ?? ''
    for (const t of [contract.hero.eyebrow, contract.hero.intro, contract.business.title, contract.clients.title, contract.steps.title, contract.catalog.subtitle, fillOperatorTemplate(contract.faq[0].answer)]) {
      expect(text).toContain(t)
    }
  })

  it('каталог: пока список грузится, каталога и строки «Ищете, куда записаться?» нет', () => {
    getPublic.mockReset().mockReturnValue(new Promise(() => {}))
    const { container } = renderPage()
    expect(container.querySelector('#companies')).toBeNull()
    expect(screen.queryByText(contract.hero.clientLink.lead)).toBeNull()
  })

  it('каталог: ошибка — одна строка role=alert с текстом контракта', async () => {
    getPublic.mockReset().mockRejectedValue(new Error('x'))
    renderPage()
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(contract.catalog.error)
  })

  it('каталог: салонов нет совсем — нет ни каталога, ни строки «Ищете, куда записаться?»', async () => {
    getPublic.mockReset().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })
    const { container } = renderPage()
    await waitFor(() => expect(getPublic).toHaveBeenCalled())
    await screen.findByText(contract.faq[0].question)
    await new Promise((r) => setTimeout(r, 0))
    expect(container.querySelector('#companies')).toBeNull()
    expect(screen.queryByText(contract.hero.clientLink.lead)).toBeNull()
    expect(screen.queryByRole('link', { name: contract.hero.clientLink.action.label })).toBeNull()
  })

  it('каталог: поиск без результатов — «Салонов пока нет», поле поиска на месте', async () => {
    getPublic.mockImplementation((p: { search?: string }) =>
      Promise.resolve(p.search ? { items: [], page: 1, pageSize: 20, total: 0, hasNext: false } : { items: [company()], page: 1, pageSize: 20, total: 1, hasNext: false }),
    )
    renderPage()
    await screen.findByText('Салон красоты')
    await userEvent.type(screen.getByPlaceholderText(contract.catalog.searchPlaceholder), 'нет такого')
    expect(await screen.findByText(contract.catalog.empty)).toBeInTheDocument()
    expect(screen.getByPlaceholderText(contract.catalog.searchPlaceholder)).toBeInTheDocument()
  })

})
