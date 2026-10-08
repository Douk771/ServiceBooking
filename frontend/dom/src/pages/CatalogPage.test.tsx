import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, render, screen, within, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CatalogPage } from './CatalogPage'
import { catalogItemFixture, catalogPageFixture, httpError } from '../test/fixtures'
import { useAuthStore } from '@/store/authStore'

const catalog = vi.hoisted(() => vi.fn())
vi.mock('../api/publicStays', () => ({ publicStaysApi: { catalog } }))

const keys: string[] = []
const nav: { search: string; back: () => void } = { search: '', back: () => undefined }
function Probe() {
  const l = useLocation()
  const navigate = useNavigate()
  nav.search = l.search
  nav.back = () => navigate(-1)
  if (keys[keys.length - 1] !== l.key) keys.push(l.key)
  return null
}

function setup(url = '/') {
  const utils = render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[url]}>
        <Probe />
        <Routes>
          <Route path="/" element={<CatalogPage />} />
          <Route path="/bookings" element={<p>bookings</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { router: nav, ...utils }
}
const search = (r: typeof nav) => r.search

beforeEach(() => {
  catalog.mockReset().mockResolvedValue(catalogPageFixture())
  keys.length = 0
  useAuthStore.setState({ token: null, user: null })
})
afterEach(() => vi.useRealTimers())

describe('CatalogPage on the template (T41-04)', () => {
  it('a) structure: one main, one h1, no footer, order, no pricing, houses section', async () => {
    const { container } = setup()
    await screen.findByText('Все дома: 1')
    expect(container.querySelectorAll('main')).toHaveLength(1)
    expect(container.querySelectorAll('h1')).toHaveLength(1)
    expect(container.querySelector('footer')).toBeNull()
    expect(container.querySelector('#pricing-title')).toBeNull()
    const ids = [container.querySelector('h1')!, container.querySelector('form[aria-label="Подбор дома"]')!, ...['houses', 'guests-title', 'biz-title', 'faq'].map((i) => container.querySelector(`#${i}`)!)]
    ids.forEach((e) => expect(e).toBeTruthy())
    for (let i = 0; i < ids.length - 1; i++) expect(Boolean(ids[i].compareDocumentPosition(ids[i + 1]) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    const houses = container.querySelector('section#houses')!
    expect(houses.getAttribute('aria-label')).toBe('Дома в Шерегеше')
    expect(within(houses as HTMLElement).getByRole('heading', { level: 2 }).textContent).toBe('Дома в Шерегеше')
    const live = container.querySelector('[aria-live="polite"]')!
    expect(live.querySelector('ul')).toBeTruthy()
    expect(live.querySelector('form')).toBeNull()
    expect(container.querySelectorAll('[data-testid="landing-screenshot-placeholder"]')).toHaveLength(1)
    expect(container.querySelector('#guests-title')!.closest('section')!.querySelector('[data-testid="landing-screenshot-placeholder"]')).toBeTruthy()
  })

  it('b) fields are labelled; min of the dates', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2027, 0, 10, 12))
    setup('/?checkIn=2027-02-01&checkOut=2027-02-05')
    const inEl = screen.getByLabelText('Заезд') as HTMLInputElement
    const outEl = screen.getByLabelText('Выезд') as HTMLInputElement
    expect(inEl.min).toBe('2027-01-10')
    expect(outEl.min).toBe('2027-02-02')
    expect(screen.getByLabelText('Гостей')).toBeTruthy()
    expect(screen.getByLabelText('Цена за ночь до, ₽')).toBeTruthy()
  })

  it('c) URL fills the form and drives exactly one request', async () => {
    setup('/?checkIn=2027-02-01&checkOut=2027-02-05&guests=3&maxPrice=5000&page=2')
    await screen.findByText(/Свободно на ваши даты/)
    expect((screen.getByLabelText('Заезд') as HTMLInputElement).value).toBe('2027-02-01')
    expect((screen.getByLabelText('Гостей') as HTMLInputElement).value).toBe('3')
    expect((screen.getByLabelText('Цена за ночь до, ₽') as HTMLInputElement).value).toBe('5000')
    expect(catalog).toHaveBeenCalledTimes(1)
    expect(catalog).toHaveBeenCalledWith({ checkIn: '2027-02-01', checkOut: '2027-02-05', guests: 3, maxPricePerNight: 5000, page: 2, pageSize: 12 })
  })

  it('d) broken params are dropped', async () => {
    setup('/?checkIn=2027-02-05&checkOut=2027-02-01&guests=0&maxPrice=abc')
    await screen.findByText('Все дома: 1')
    expect(catalog).toHaveBeenCalledWith({ guests: 1, page: 1, pageSize: 12 })
  })

  it('e) date rules and hints', async () => {
    const { router } = setup()
    await screen.findByText('Все дома: 1')
    const inEl = screen.getByLabelText('Заезд')
    const outEl = screen.getByLabelText('Выезд')
    const status = screen.getByRole('status')
    fireEvent.change(inEl, { target: { value: '2027-03-01' } })
    expect(status).toHaveTextContent('Укажите обе даты: заезд и выезд')
    fireEvent.change(outEl, { target: { value: '2027-02-20' } })
    expect(status).toHaveTextContent('Дата выезда должна быть позже даты заезда')
    fireEvent.change(outEl, { target: { value: '2027-03-04' } })
    expect(status).toHaveTextContent('')
    expect(search(router)).toBe('?checkIn=2027-03-01&checkOut=2027-03-04')
    fireEvent.change(inEl, { target: { value: '2027-03-10' } })
    expect((outEl as HTMLInputElement).value).toBe('')
    expect(status).toHaveTextContent('Укажите обе даты')
  })

  it('e2) paging is reset when dates change', async () => {
    const { router } = setup('/?page=2')
    await screen.findByText('Все дома: 1')
    fireEvent.change(screen.getByLabelText('Заезд'), { target: { value: '2027-03-01' } })
    fireEvent.change(screen.getByLabelText('Выезд'), { target: { value: '2027-03-04' } })
    expect(search(router)).not.toContain('page')
  })

  it('f) guests typing writes one history entry; back restores form and request', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    const { router } = setup()
    await act(async () => { await vi.advanceTimersByTimeAsync(10) })
    const before = keys.length
    const g = screen.getByLabelText('Гостей')
    fireEvent.change(g, { target: { value: '1' } })
    fireEvent.change(g, { target: { value: '12' } })
    await act(async () => { await vi.advanceTimersByTimeAsync(450) })
    expect(keys.length).toBe(before + 1)
    expect(search(router)).toBe('?guests=12')
    expect(catalog).toHaveBeenLastCalledWith({ guests: 12, page: 1, pageSize: 12 })
    act(() => router.back())
    await act(async () => { await vi.advanceTimersByTimeAsync(450) })
    expect((screen.getByLabelText('Гостей') as HTMLInputElement).value).toBe('1')
    expect(search(router)).toBe('')
    expect(catalog).toHaveBeenLastCalledWith({ guests: 1, page: 1, pageSize: 12 })
  })

  it('g) reset button only with filters; clears URL and hint', async () => {
    const { router } = setup()
    await screen.findByText('Все дома: 1')
    expect(screen.queryByRole('button', { name: 'Сбросить фильтры' })).toBeNull()
    fireEvent.change(screen.getByLabelText('Заезд'), { target: { value: '2027-03-01' } })
    fireEvent.change(screen.getByLabelText('Выезд'), { target: { value: '2027-03-04' } })
    await userEvent.click(screen.getByRole('button', { name: 'Сбросить фильтры' }))
    expect(search(router)).toBe('')
    expect(screen.getByRole('status')).toHaveTextContent('')
  })

  it('h1) loading, error with retry', async () => {
    catalog.mockReturnValue(new Promise(() => undefined))
    const a = setup()
    expect(a.container.querySelector('[aria-live="polite"] .animate-pulse, [aria-live="polite"] [class*="h-[360px]"]')).toBeTruthy()
    a.unmount()
    catalog.mockReset().mockRejectedValueOnce(httpError(500, '')).mockResolvedValue(catalogPageFixture())
    setup()
    expect(await screen.findByRole('alert')).toBeTruthy()
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(await screen.findByText('Все дома: 1')).toBeTruthy()
  })

  it('h2) empty states without and with filters', async () => {
    catalog.mockResolvedValue(catalogPageFixture([]))
    const a = setup()
    expect(await screen.findByText('Пока нет домов в каталоге')).toBeTruthy()
    expect(screen.getByText('Как только владельцы опубликуют дома, они появятся здесь.')).toBeTruthy()
    a.unmount()
    const { router } = setup('/?guests=3')
    expect(await screen.findByText('По этим условиям домов нет')).toBeTruthy()
    expect(screen.getByText('Попробуйте другие даты, меньше гостей или более высокую цену — или сбросьте фильтры.')).toBeTruthy()
    const buttons = screen.getAllByRole('button', { name: 'Сбросить фильтры' })
    expect(buttons).toHaveLength(2)
    await userEvent.click(buttons[1])
    expect(search(router)).toBe('')
  })

  it('h3) counter for dates and pagination', async () => {
    catalog.mockResolvedValue(catalogPageFixture([catalogItemFixture()], { totalCount: 30 }))
    const { router } = setup('/?checkIn=2027-02-01&checkOut=2027-02-05')
    expect(await screen.findByText('Свободно на ваши даты: 30')).toBeTruthy()
    const next = screen.getByRole('button', { name: /Вперёд|Далее|следующ/i })
    await userEvent.click(next)
    expect(search(router)).toContain('page=2')
  })

  it('i) card link has dates and adults', async () => {
    setup('/?checkIn=2027-02-01&checkOut=2027-02-05&guests=2')
    await screen.findByText(/Свободно на ваши даты/)
    const href = screen.getByRole('link', { name: /Дом у склона/ }).getAttribute('href')!
    expect(href).toContain('checkIn=2027-02-01')
    expect(href).toContain('adults=2')
  })

  it('j) buttons and anchors', async () => {
    setup()
    await screen.findByText('Все дома: 1')
    expect(screen.getByRole('link', { name: 'Как забронировать' }).getAttribute('href')).toBe('#guests-title')
    expect(screen.getByRole('link', { name: 'Выбрать дом' }).getAttribute('href')).toBe('#houses')
    expect(screen.getByRole('link', { name: 'Мои брони' }).getAttribute('href')).toBe('/bookings')
    expect(screen.getByRole('link', { name: /Подключить дома/ }).getAttribute('href')).toBe('/register?returnTo=%2Fcabinet%2Fnew')
    expect(screen.getByRole('link', { name: 'Войти в кабинет' }).getAttribute('href')).toBe('/cabinet')
  })

  it('j2) signed-in owner goes straight to the new company', async () => {
    useAuthStore.setState({ token: 't' })
    setup()
    await screen.findByText('Все дома: 1')
    expect(screen.getByRole('link', { name: /Подключить дома/ }).getAttribute('href')).toBe('/cabinet/new')
  })

  it('k) FAQ is collapsed and has no links', async () => {
    const { container } = setup()
    await screen.findByText('Все дома: 1')
    const faq = container.querySelector('#faq')!
    const btns = faq.querySelectorAll('button[aria-expanded="false"]')
    expect(btns).toHaveLength(8)
    expect(faq.querySelectorAll('a')).toHaveLength(0)
  })

  it('l) very long name and address are shown whole', async () => {
    const name = 'Н'.repeat(200)
    const address = 'А'.repeat(300)
    catalog.mockResolvedValue(catalogPageFixture([catalogItemFixture({ houseName: name, address })]))
    setup()
    expect(await screen.findByText(name)).toBeTruthy()
    expect(screen.getByText(address)).toBeTruthy()
  })
})
