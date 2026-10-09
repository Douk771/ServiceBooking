import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LandingClientsSection } from '@/components/landing/LandingClientsSection'
import { goodsLanding } from './goodsLanding'
import { useAuthStore } from '@/store/authStore'
import manifest from '../assets/screenshots/screenshots.json'

const H2 = 'Соберите заказ с телефона и заберите, когда он готов'
const PARAGRAPH =
  'Выберите магазин или кафе, добавьте товары в корзину и укажите, когда удобно забрать. Звонить не нужно: магазин увидит заказ сразу, а вы — когда он будет готов.'
const STEPS = [
  ['Выберите магазин', 'Найдите магазин или кафе в каталоге своего города или откройте его по ссылке либо QR-коду.'],
  ['Соберите корзину', 'Добавьте товары — поштучно или на вес. Сумма за весовой товар уточнится при выдаче.'],
  ['Выберите время и оформите', 'Укажите, когда заберёте заказ — как можно скорее или к удобному часу. Всё на одном экране.'],
]
const TRACK = [
  'После оформления откроется страница заказа: номер, статус и время получения.',
  'Страница обновляется сама — вы увидите, когда магазин примет заказ и когда он будет готов к выдаче.',
  'Включите уведомление на странице заказа, если браузер это разрешает, — тогда о смене статуса сообщим без обновления страницы.',
  'Сохраните ссылку на заказ. Если вы вошли в аккаунт, все заказы будут в разделе «Мои заказы».',
  'Если магазин изменит состав заказа, на странице будет видно, что было и что стало.',
]
const ORDER_ALT = `Страница заказа на телефоне: заказ № ${manifest.orderPage.orderNumber} принят магазином, получение сегодня к ${manifest.orderPage.pickupClock}, отмечены шаги „Заказ оформлен“ и „Магазин принял заказ“`

const setup = () => render(<MemoryRouter><LandingClientsSection config={goodsLanding.clients} placeholder={false} /></MemoryRouter>)
const signIn = () =>
  useAuthStore.setState({ user: { id: 'u', phone: '79001234567', firstName: 'И', lastName: 'П', roles: ['Client'] }, token: 't' })

beforeEach(() => useAuthStore.setState({ user: null, token: null }))
afterEach(() => useAuthStore.setState({ user: null, token: null }))

describe('BuyersBlock', () => {
  it('T30-01 shows the eyebrow', () => {
    setup()
    expect(screen.getByText('Для покупателей')).toBeTruthy()
  })

  it('T30-02 h2 has id buyers-title, exact text and tabIndex -1', () => {
    setup()
    const h2 = screen.getByRole('heading', { level: 2 })
    expect(h2.id).toBe('buyers-title')
    expect(h2.textContent).toBe(H2)
    expect(h2.tabIndex).toBe(-1)
  })

  it('T30-03 section is a region labelled by h2', () => {
    setup()
    expect(screen.getByRole('region', { name: H2 }).getAttribute('aria-labelledby')).toBe('buyers-title')
  })

  it('T30-04 paragraph verbatim', () => {
    setup()
    expect(screen.getByText(PARAGRAPH)).toBeTruthy()
  })

  it('T30-05 heading order', () => {
    setup()
    const hs = screen.getAllByRole('heading').map((h) => [h.tagName, h.textContent])
    expect(hs).toEqual([
      ['H2', H2],
      ['H3', 'Как сделать заказ'],
      ['H4', STEPS[0][0]],
      ['H4', STEPS[1][0]],
      ['H4', STEPS[2][0]],
      ['H3', 'Как следить за заказом'],
    ])
  })

  it('T30-06 steps: ol with 3 li verbatim, short, with icons', () => {
    const { container } = setup()
    const ol = container.querySelector('ol') as HTMLElement
    const items = within(ol).getAllByRole('listitem')
    expect(items).toHaveLength(3)
    items.forEach((li, i) => {
      expect(li.querySelector('h4')?.textContent).toBe(STEPS[i][0])
      expect(li.querySelector('p')?.textContent).toBe(STEPS[i][1])
      expect(STEPS[i][1].length).toBeLessThanOrEqual(140)
      expect(li.querySelector('svg')).not.toBeNull()
    })
  })

  it('T30-07 track: ul with 5 li verbatim, short, hidden icons', () => {
    const { container } = setup()
    const ul = container.querySelector('ul') as HTMLElement
    expect(ul.getAttribute('aria-labelledby')).toBe('buyers-track-title')
    const items = within(ul).getAllByRole('listitem')
    expect(items).toHaveLength(5)
    items.forEach((li, i) => {
      expect(li.textContent).toBe(TRACK[i])
      expect(TRACK[i].length).toBeLessThanOrEqual(140)
      expect(li.querySelector('svg[aria-hidden="true"]')).not.toBeNull()
    })
  })

  it('T30-08 payment line verbatim', () => {
    setup()
    expect(screen.getByText('Оплата — при получении в магазине.')).toBeTruthy()
  })

  it('T30-09 buttons: anchors and orders link for anonymous and signed-in, before the steps list', () => {
    const { container, unmount } = setup()
    const shop = screen.getByRole('link', { name: 'Выбрать магазин' })
    expect(shop.getAttribute('href')).toBe('#shop-list')
    expect(shop.className).toContain('bg-ink')
    expect(shop.querySelector('svg')).not.toBeNull()
    expect(screen.getByRole('link', { name: 'Мои заказы' }).getAttribute('href')).toBe('/orders')
    expect(shop.parentElement?.className).toContain('flex-wrap')
    const ol = container.querySelector('ol') as HTMLElement
    expect(shop.compareDocumentPosition(ol) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    unmount()
    signIn()
    setup()
    expect(screen.getByRole('link', { name: 'Мои заказы' }).getAttribute('href')).toBe('/orders')
    expect(screen.getByRole('link', { name: 'Выбрать магазин' }).getAttribute('href')).toBe('#shop-list')
  })

  it('T30-10 all svg are aria-hidden', () => {
    const { container } = setup()
    const svgs = container.querySelectorAll('section svg')
    expect(svgs.length).toBeGreaterThan(0)
    svgs.forEach((s) => expect(s.getAttribute('aria-hidden')).toBe('true'))
  })

  it('T30-11 no forbidden words; the notification root only in track[2]', () => {
    const { container } = setup()
    const text = container.textContent ?? ''
    expect(text).not.toMatch(/столов/i)
    expect(text).not.toMatch(/бесплатн/i)
    expect(text).not.toMatch(/комисси/i)
    expect(text).not.toMatch(/whatsapp/i)
    expect(text).not.toMatch(/\bMAX\b/)
    const withRoot = Array.from(container.querySelectorAll('li')).filter((li) => /уведомл/i.test(li.textContent ?? ''))
    expect(withRoot).toHaveLength(1)
    expect(withRoot[0].textContent).toBe(TRACK[2])
    expect(text.match(/уведомл/gi)).toHaveLength(1)
  })

  it('T30-12 order screenshot: alt, size, lazy loading, 1x/2x, caption', () => {
    const { container } = setup()
    const img = container.querySelector('figure img') as HTMLImageElement
    expect(img.getAttribute('alt')).toBe(ORDER_ALT)
    expect(img.getAttribute('alt')).toContain(String(manifest.orderPage.orderNumber))
    expect(img.getAttribute('width')).toBe(String(manifest.orderPage.cssWidth))
    expect(img.getAttribute('height')).toBe(String(manifest.orderPage.cssHeight))
    expect(img.getAttribute('loading')).toBe('lazy')
    expect(img.getAttribute('decoding')).toBe('async')
    const srcset = img.getAttribute('srcset') ?? ''
    expect(srcset).toContain(' 1x')
    expect(srcset).toContain(' 2x')
    expect(container.querySelector('figcaption')?.textContent).toBe('Так выглядит страница заказа: статус меняется сам')
  })
})
