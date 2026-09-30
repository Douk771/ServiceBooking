import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { BusinessBlock } from './BusinessBlock'
import { useAuthStore } from '@/store/authStore'

const H2 = 'Магазин и кафе принимают заказы без звонков и переписок'
const PARAGRAPH =
  'Покупатели заказывают сами — по ссылке, QR-коду на кассе или из каталога goods. Вам больше не нужно принимать заказы по телефону и искать их по чатам: все заказы собраны в одном месте.'
const BENEFITS = [
  'Все заказы на одном экране — на планшете у кассы, компьютере или телефоне, со звуком о новом заказе.',
  'Покупатель сам выбирает время получения, а вы заранее видите, что и к какому часу собрать.',
  'Весовой товар — по точной сумме: итог считается по фактическому весу при выдаче.',
  'Покупатель видит статус заказа у себя в телефоне и не звонит спросить, готово ли.',
]
const STEP_TITLES = ['Соберите каталог', 'Поделитесь ссылкой или QR', 'Принимайте и выдавайте']

const setup = () => render(<MemoryRouter><BusinessBlock /></MemoryRouter>)
const signIn = () =>
  useAuthStore.setState({ user: { id: 'u', phone: '79001234567', firstName: 'И', lastName: 'П', roles: ['Client'] }, token: 't' })

beforeEach(() => useAuthStore.setState({ user: null, token: null }))
afterEach(() => useAuthStore.setState({ user: null, token: null }))

describe('BusinessBlock', () => {
  it('T27-01 shows the eyebrow', () => {
    setup()
    expect(screen.getByText('Для бизнеса')).toBeTruthy()
  })

  it('T27-02 h2 has id biz-title and the exact text', () => {
    setup()
    const h2 = screen.getByRole('heading', { level: 2 })
    expect(h2.id).toBe('biz-title')
    expect(h2.textContent).toBe(H2)
    expect(h2.textContent).toContain('кафе')
  })

  it('T27-03 section is a region labelled by h2', () => {
    setup()
    const region = screen.getByRole('region', { name: H2 })
    expect(region.getAttribute('aria-labelledby')).toBe('biz-title')
  })

  it('T27-04 has no forbidden words', () => {
    const { container } = setup()
    const text = container.textContent ?? ''
    expect(text).not.toMatch(/столов/i)
    expect(text).not.toMatch(/бесплатн/i)
    expect(text).not.toMatch(/комисси/i)
  })

  it('T27-05 paragraph and 4 benefits verbatim', () => {
    const { container } = setup()
    expect(screen.getByText(PARAGRAPH)).toBeTruthy()
    const ul = container.querySelector('ul') as HTMLElement
    const items = within(ul).getAllByRole('listitem')
    expect(items).toHaveLength(4)
    BENEFITS.forEach((b, i) => expect(items[i].textContent).toBe(b))
  })

  it('T27-06 steps: h3, ol with 3 li, three h4 in order', () => {
    const { container } = setup()
    expect(screen.getByRole('heading', { level: 3, name: 'Как начать принимать заказы' })).toBeTruthy()
    const ol = container.querySelector('ol') as HTMLElement
    expect(within(ol).getAllByRole('listitem')).toHaveLength(3)
    const h4 = screen.getAllByRole('heading', { level: 4 })
    expect(h4.map((h) => h.textContent)).toEqual(STEP_TITLES)
  })

  it('T27-07 anonymous: connect link goes to register with returnTo', () => {
    setup()
    expect(screen.getByRole('link', { name: 'Подключить магазин' }).getAttribute('href')).toBe('/register?returnTo=%2Fcabinet%2Fnew')
  })

  it('T27-08 signed-in: connect link goes to /cabinet/new', () => {
    signIn()
    setup()
    expect(screen.getByRole('link', { name: 'Подключить магазин' }).getAttribute('href')).toBe('/cabinet/new')
  })

  it('T27-09 cabinet link goes to /cabinet for anonymous and signed-in', () => {
    const { unmount } = setup()
    expect(screen.getByRole('link', { name: 'Войти в кабинет' }).getAttribute('href')).toBe('/cabinet')
    unmount()
    signIn()
    setup()
    expect(screen.getByRole('link', { name: 'Войти в кабинет' }).getAttribute('href')).toBe('/cabinet')
  })

  it('T27-10 all svg are aria-hidden', () => {
    const { container } = setup()
    const svgs = container.querySelectorAll('section svg')
    expect(svgs.length).toBeGreaterThan(0)
    svgs.forEach((s) => expect(s.getAttribute('aria-hidden')).toBe('true'))
  })
})
