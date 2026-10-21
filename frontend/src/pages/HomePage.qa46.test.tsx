import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import type { Company } from '../types'
import { useAuthStore } from '../store/authStore'

// QA цикла 46: независимые сценарии по критериям приёмки SPEC.md (US-46-01..08). Все ожидаемые строки
// продублированы литералами из SPEC.md §4, а не берутся из contracts/cycle46 или из конфигов.

const getPublic = vi.fn()
const getPublicPricing = vi.fn()

vi.mock('../api/companies', () => ({ companiesApi: { getPublic: (...a: unknown[]) => getPublic(...a) } }))
vi.mock('../api/pricing', () => ({ pricingApi: { getPublicPricing: (...a: unknown[]) => getPublicPricing(...a) } }))

const company = (o: Partial<Company> = {}): Company => ({ id: 'c1', name: 'Салон красоты', slug: 'salon', allowSelfBooking: true, ...o })

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

const pricingDto = {
  version: '1', currency: 'RUB', notice: '', legalNotice: null, options: [],
  plans: [
    { id: 'f', name: 'Старт', description: null, pricePerMonth: 0, highlights: [], sortOrder: 1, isFree: true, isTrial: false, includedCompanies: 1, includedEmployees: 3 },
    { id: 'p', name: 'Салон', description: null, pricePerMonth: 990, highlights: [], sortOrder: 2, isFree: false, isTrial: false, includedCompanies: 1, includedEmployees: 10 },
  ],
}

const follows = (a: Element, b: Element) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)

beforeEach(() => {
  localStorage.clear()
  window.location.hash = ''
  useAuthStore.setState({ user: null, token: null })
  getPublic.mockReset().mockResolvedValue({ items: [company()], page: 1, pageSize: 20, total: 1, hasNext: false })
  getPublicPricing.mockReset().mockResolvedValue(null)
})

describe('US-46-01: первый экран для владельца', () => {
  it('заголовок, абзац и кнопка по SPEC §4.1; ровно один h1', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const h1s = container.querySelectorAll('h1')
    expect(h1s).toHaveLength(1)
    expect(h1s[0].textContent).toContain('Онлайн-запись в ваш салон')
    expect(h1s[0].textContent).toContain('без звонков и переписок')
    const text = container.textContent ?? ''
    expect(text).toContain('Онлайн-запись для салонов и мастеров')
    expect(text).toContain(
      'Клиент сам выбирает услугу, мастера и свободное время, а запись сразу появляется в расписании. Не нужно отвечать на звонки и сверять тетрадь с мессенджерами.',
    )
  })

  it('«Подключить салон»: гость -> /register?returnTo, вошедший -> /cabinet/new; в обоих блоках одинаково', async () => {
    const { container, unmount } = renderPage()
    await screen.findByText('Салон красоты')
    const guest = screen.getAllByRole('link', { name: 'Подключить салон' })
    expect(guest.length).toBeGreaterThanOrEqual(2)
    guest.forEach((a) => expect(a).toHaveAttribute('href', '/register?returnTo=%2Fcabinet%2Fnew'))
    expect(container.querySelectorAll('a[href="/cabinet/new"]')).toHaveLength(0)
    unmount()
    useAuthStore.setState({ user: { id: 'u', phone: '7', firstName: 'a', lastName: 'b', roles: ['Client'] }, token: 't' })
    renderPage()
    await screen.findByText('Салон красоты')
    const authed = screen.getAllByRole('link', { name: 'Подключить салон' })
    expect(authed.length).toBeGreaterThanOrEqual(2)
    authed.forEach((a) => expect(a).toHaveAttribute('href', '/cabinet/new'))
  })

  it('ссылки «Как записываются клиенты» нет; строка клиента -> #companies, это ссылка внутри текста, а не кнопка-карточка', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    expect(screen.queryByRole('link', { name: 'Как записываются клиенты' })).toBeNull()
    const link = screen.getByRole('link', { name: 'Салоны на EZBOOK' })
    expect(link).toHaveAttribute('href', '#companies')
    const line = link.parentElement as HTMLElement
    expect(line.textContent).toContain('Ищете, куда записаться?')
    expect(line.tagName).toBe('P')
    expect(link.className).not.toMatch(/\bborder\b|rounded|shadow|\bbg-/)
    expect(container.querySelector('#clients-title')).not.toBeNull()
  })

  it('в первом экране нет «проверенный» и «бесплатно»', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const h1 = container.querySelector('h1') as HTMLElement
    // первый экран = всё до первой секции с id/aria-labelledby=biz-title
    const biz = container.querySelector('[aria-labelledby="biz-title"]') as HTMLElement
    let hero = ''
    for (const el of Array.from(container.querySelectorAll('main > *'))) {
      if (el === biz || el.contains(biz) || follows(biz, el)) break
      hero += el.textContent
    }
    expect(hero).toContain(h1.textContent!)
    expect(hero.toLowerCase()).not.toMatch(/проверен|бесплатн/)
  })
})

describe('US-46-02: порядок блоков', () => {
  it('без тарифов: h1 -> biz -> clients -> steps -> faq -> companies, #pricing-title нет', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    expect(container.querySelector('#pricing-title')).toBeNull()
    const seq = ['h1', '#biz-title', '#clients-title', '#biz-steps-title', '#faq-title', '#companies'].map((s) => container.querySelector(s)!)
    seq.forEach((e) => expect(e).not.toBeNull())
    for (let i = 1; i < seq.length; i++) expect(follows(seq[i - 1], seq[i])).toBe(true)
  })

  it('с опубликованными тарифами: тарифы между «Как начать» и FAQ; строка «от цены»', async () => {
    getPublicPricing.mockResolvedValue(pricingDto)
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('#pricing-title')).not.toBeNull())
    const seq = ['h1', '#biz-title', '#clients-title', '#biz-steps-title', '#pricing-title', '#faq-title', '#companies'].map((s) => container.querySelector(s)!)
    for (let i = 1; i < seq.length; i++) expect(follows(seq[i - 1], seq[i])).toBe(true)
    expect(container.textContent).toMatch(/Онлайн-запись для салона — от\s*990/)
  })

  it('каталог — последняя секция страницы', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const sections = container.querySelectorAll('main > section')
    expect(sections[sections.length - 1].id).toBe('companies')
  })

  it('иерархия заголовков без пропусков: h1, затем h2 секций, внутри шагов h3', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const levels = Array.from(container.querySelectorAll('h1,h2,h3,h4,h5,h6')).map((h) => Number(h.tagName[1]))
    for (let i = 1; i < levels.length; i++) expect(levels[i] - levels[i - 1]).toBeLessThanOrEqual(1)
    for (const id of ['biz-title', 'clients-title', 'biz-steps-title', 'faq-title']) {
      const section = container.querySelector(`[aria-labelledby="${id}"]`)
      expect(section, id).not.toBeNull()
    }
  })

  it('#biz-title прокручивается при переходе с хешем (about.ezbook.ru)', async () => {
    const spy = vi.fn()
    Element.prototype.scrollIntoView = spy
    window.location.hash = '#biz-title'
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    await waitFor(() => expect(spy).toHaveBeenCalled())
    expect(spy.mock.instances[0]).toBe(container.querySelector('#biz-title'))
  })

  it('ни одно предложение не повторяется дословно в двух блоках (кроме двух разрешённых)', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const allowed = new Set(['Подключить салон', 'Салоны на EZBOOK'])
    const seen = new Map<string, string>()
    const dups: string[] = []
    const roots = Array.from(container.querySelectorAll('main > *')).filter((e) => e.tagName === 'SECTION' || e.querySelector('h1'))
    roots.forEach((root, idx) => {
      const leaves = Array.from(root.querySelectorAll('*')).filter((e) => e.children.length === 0 && (e.textContent ?? '').trim().length > 12)
      for (const l of leaves) {
        const t = l.textContent!.trim()
        if (allowed.has(t)) continue
        const prev = seen.get(t)
        if (prev !== undefined && prev !== String(idx)) dups.push(t)
        seen.set(t, String(idx))
      }
    })
    expect(dups).toEqual([])
  })
})

describe('US-46-03/04: блоки «клиенты» и «как начать»', () => {
  it('«Для салона»: заголовок, абзац, 7 возможностей, обе кнопки, скриншот с подписью', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const biz = container.querySelector('[aria-labelledby="biz-title"]') as HTMLElement
    const t = biz.textContent ?? ''
    expect(t).toContain('Для салона')
    expect(t).toContain('Все записи и расписание мастеров — в одном месте')
    expect(t).toContain('Клиенты записываются по ссылке, со страницы салона в каталоге или через форму на вашем сайте. Каждая запись сразу попадает в расписание нужного мастера.')
    for (const f of [
      'Страница салона с онлайн-записью и место в каталоге EZBOOK.',
      'Расписание мастеров по датам и шаблоны рабочей недели.',
      'Перенос и отмена записи клиентом — по вашим правилам.',
      'База клиентов с заметками и фото.',
      'Отзывы клиентов после визита.',
      'Форма записи для вашего сайта.',
      'Отчёты по выручке и комиссиям мастеров — на платных тарифах.',
    ]) expect(t).toContain(f)
    expect(within(biz).getByRole('link', { name: 'Войти в кабинет' })).toHaveAttribute('href', '/cabinet')
    expect(within(biz).getByRole('link', { name: 'Подключить салон' })).toBeInTheDocument()
    expect(t).toContain('Записи мастера на день: клиент, услуга и время')
  })

  it('«Клиенту хватит минуты и телефона»: обращение к владельцу, нет панели шагов, ссылок и кнопок; карусель на месте', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const c = container.querySelector('[aria-labelledby="clients-title"]') as HTMLElement
    const t = c.textContent ?? ''
    expect(t).toContain('Для ваших клиентов')
    expect(t).toContain('Клиенту хватит минуты и телефона')
    expect(t).toContain('Регистрация не нужна: несколько коротких шагов — и клиент сразу видит подтверждение записи.')
    expect(c.querySelector('a')).toBeNull()
    expect(within(c).queryByText('Найти специалиста')).toBeNull()
    expect(within(c).queryByText('Мои визиты')).toBeNull()
    expect(t).not.toContain('Как записаться')
    const region = within(c).getByRole('region', { name: 'Шаг за шагом' })
    expect(region.querySelectorAll('[data-testid="carousel-slide"]').length).toBe(6)
  })

  it('карусель: клик по шагу переключает кадр, остаётся один активный', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const region = screen.getByRole('region', { name: 'Шаг за шагом' })
    const steps = region.querySelectorAll('ol button')
    expect(steps).toHaveLength(6)
    await userEvent.click(steps[4])
    expect(steps[4]).toHaveAttribute('aria-current', 'true')
    expect(steps[0]).not.toHaveAttribute('aria-current', 'true')
    expect(container.querySelectorAll('[data-testid="carousel-slide"][data-active]')).toHaveLength(1)
  })

  it('«Что ещё может клиент»: 5 пунктов дословно + примечание об оплате; ничего из прежних фактов не потеряно', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const list = container.querySelector('ul[aria-labelledby="clients-list-title"]') as HTMLElement
    expect(container.querySelector('#clients-list-title')!.textContent).toBe('Что ещё может клиент')
    expect(Array.from(list.querySelectorAll('li')).map((l) => l.textContent!.trim())).toEqual([
      'Записаться на несколько услуг за один визит — до пяти.',
      'Записать ребёнка или другого человека.',
      'Перенести или отменить запись в «Моих визитах» — по правилам салона.',
      'Получить напоминание накануне визита.',
      'Оставить отзыв после визита.',
    ])
    expect(container.textContent).toContain('Оплата — в салоне, EZBOOK деньги клиентов не принимает.')
  })

  it('«Как начать принимать записи»: 3 шага владельца после блока клиентов и перед FAQ', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const s = container.querySelector('[aria-labelledby="biz-steps-title"]') as HTMLElement
    expect(s.querySelector('#biz-steps-title')!.textContent).toBe('Как начать принимать записи')
    const t = s.textContent ?? ''
    for (const x of [
      'Заведите салон и услуги', 'Название, адрес, услуги с ценами и длительностью.',
      'Добавьте мастеров', 'Пригласите мастеров и задайте им рабочие дни и часы.',
      'Поделитесь ссылкой', 'Отправьте клиентам ссылку на страницу салона или встройте форму записи на свой сайт.',
    ]) expect(t).toContain(x)
    expect(s.querySelectorAll('li')).toHaveLength(3)
    expect(container.querySelector('#biz-steps-title')).not.toBeNull()
    expect(follows(container.querySelector('#clients-title')!, s)).toBe(true)
    expect(follows(s, container.querySelector('#faq-title')!)).toBe(true)
  })
})

describe('US-46-05: каталог', () => {
  it('заголовок, подзаголовок, поиск шириной контейнера (не 280px)', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const cat = container.querySelector('#companies') as HTMLElement
    expect(within(cat).getByRole('heading', { level: 2, name: 'Салоны на EZBOOK' })).toBeInTheDocument()
    expect(cat.textContent).toContain('Выберите салон и запишитесь онлайн')
    expect(cat.textContent?.toLowerCase()).not.toContain('проверен')
    const input = within(cat).getByPlaceholderText('Поиск по названию или адресу')
    expect(input.className).toContain('w-full')
  })

  it('поиск отправляет запрос и сбрасывает на первую страницу; плашка «Онлайн-запись» видна', async () => {
    getPublic.mockResolvedValue({
      items: [company({ onlineBookingEnabled: true }), company({ id: 'c2', name: 'Демо-студия', slug: 'demo', onlineBookingEnabled: true, isShowcase: true, showcaseBookingOpen: true })],
      page: 1, pageSize: 20, total: 2, hasNext: false,
    })
    renderPage()
    await screen.findByText('Салон красоты')
    expect(screen.getAllByText('Онлайн-запись').length).toBeGreaterThanOrEqual(2)
    await userEvent.type(screen.getByPlaceholderText('Поиск по названию или адресу'), 'Роза')
    await waitFor(() => {
      const last = getPublic.mock.calls[getPublic.mock.calls.length - 1][0]
      expect(JSON.stringify(last)).toContain('Роза')
      expect(last.page).toBe(1)
    })
  })

  it('загрузка первой страницы — каталога нет; ошибка — одна строка с иконкой', async () => {
    getPublic.mockReset().mockReturnValue(new Promise(() => {}))
    const first = renderPage()
    expect(first.container.querySelector('#companies')).toBeNull()
    first.unmount()

    getPublic.mockReset().mockRejectedValue(new Error('x'))
    const err = renderPage()
    const alert = await err.findByRole('alert')
    expect(alert).toHaveTextContent('Не удалось загрузить список компаний. Попробуйте обновить страницу.')
    expect(alert.querySelector('svg')).not.toBeNull()
  })
})

describe('US-46-07/08: тексты и FAQ', () => {
  it('на странице нет оценочных формулировок', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const html = (container.textContent ?? '') + Array.from(container.querySelectorAll('[aria-label],img')).map((e) => e.getAttribute('aria-label') + ' ' + e.getAttribute('alt')).join(' ')
    expect(html).not.toMatch(/проверенн|лучш|№ ?1|самый/i)
  })

  it('FAQ: 8 вопросов в порядке SPEC §4.6; первый содержит ссылку на /pricing', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const qs = Array.from(container.querySelectorAll('#faq h3 > button')).map((b) => b.textContent!.trim())
    expect(qs).toEqual([
      'Сколько стоит EZBOOK для салона?',
      'Можно ли принимать записи на своём сайте?',
      'Нужно ли клиенту регистрироваться, чтобы записаться?',
      'Может ли клиент сам перенести или отменить запись?',
      'На сколько дней вперёд можно записаться?',
      'Сколько стоит запись для клиента?',
      'Можно ли записаться сразу на несколько услуг?',
      'Можно ли записать ребёнка или другого человека?',
    ])
    const buttons = container.querySelectorAll('#faq h3 > button')
    await userEvent.click(buttons[0])
    expect(buttons[0]).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('link', { name: 'Тарифы и цены' })).toHaveAttribute('href', '/pricing')
    await userEvent.click(buttons[2])
    expect(container.textContent).toContain('Нет. Клиент может записаться как гость: имя, телефон и проверка «я не робот». Если записаться через аккаунт, все визиты будут в разделе «Мои визиты».')
    await userEvent.click(buttons[3])
    expect(container.textContent).toContain('Да, в разделе «Мои визиты», если он записывался через аккаунт. За сколько времени до визита это ещё можно сделать, решает салон. Позже — только через салон.')
  })

  it('FAQ: второй вопрос раскрывается/сворачивается клавиатурой', async () => {
    const { container } = renderPage()
    await screen.findByText('Салон красоты')
    const b = container.querySelectorAll('#faq h3 > button')[1] as HTMLElement
    b.focus()
    await userEvent.keyboard('{Enter}')
    expect(b).toHaveAttribute('aria-expanded', 'true')
    await userEvent.keyboard('{Enter}')
    expect(b).toHaveAttribute('aria-expanded', 'false')
  })
})
