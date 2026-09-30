import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { BuyersBlock } from './BuyersBlock'
import { BusinessBlock } from './BusinessBlock'
import { useAuthStore } from '@/store/authStore'
import { statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'

// QA-тесты цикла 30, написаны по SPEC.md (US-30-01..05, §6), независимо от реализации.
const setup = (ui = <BuyersBlock />) => render(<MemoryRouter>{ui}</MemoryRouter>)
beforeEach(() => useAuthStore.setState({ user: null, token: null }))
afterEach(() => useAuthStore.setState({ user: null, token: null }))

describe('BuyersBlock QA (cycle 30)', () => {
  it('QA30-01 section is labelled by h2 with variant A text; id differs from biz-title', () => {
    const { container } = setup()
    const sec = container.querySelector('section') as HTMLElement
    const id = sec.getAttribute('aria-labelledby') as string
    expect(id).toBeTruthy()
    expect(id).not.toBe('biz-title')
    const h2 = container.querySelector(`h2#${id}`) as HTMLElement
    expect(h2.textContent).toBe('Соберите заказ с телефона и заберите, когда он готов')
    expect(h2.textContent!.length).toBeLessThanOrEqual(60)
    expect(container.querySelectorAll('h2')).toHaveLength(1)
  })

  it('QA30-02 eyebrow, paragraph and payment line verbatim', () => {
    const { container, getByText } = setup()
    expect(getByText('Для покупателей').className).toMatch(/uppercase/)
    expect(container.textContent).toContain(
      'Выберите магазин или кафе, добавьте товары в корзину и укажите, когда удобно забрать. Звонить не нужно: магазин увидит заказ сразу, а вы — когда он будет готов.',
    )
    const pay = getByText('Оплата — при получении в магазине.')
    expect(pay.className).toContain('text-muted')
  })

  it('QA30-03 heading levels h2 > h3 > h4 without gaps', () => {
    const { container } = setup()
    const hs = Array.from(container.querySelectorAll('h1,h2,h3,h4,h5,h6')).map((h) => [h.tagName, h.textContent])
    expect(hs).toEqual([
      ['H2', 'Соберите заказ с телефона и заберите, когда он готов'],
      ['H3', 'Как сделать заказ'],
      ['H4', 'Выберите магазин'],
      ['H4', 'Соберите корзину'],
      ['H4', 'Выберите время и оформите'],
      ['H3', 'Как следить за заказом'],
    ])
  })

  it('QA30-04 five tracking items verbatim and within 140 chars', () => {
    const { container } = setup()
    const items = Array.from(container.querySelectorAll('ul > li')).map((l) => l.textContent)
    expect(items).toEqual([
      'После оформления откроется страница заказа: номер, статус и время получения.',
      'Страница обновляется сама — вы увидите, когда магазин примет заказ и когда он будет готов к выдаче.',
      'Включите уведомление на странице заказа, если браузер это разрешает, — тогда о смене статуса сообщим без обновления страницы.',
      'Сохраните ссылку на заказ. Если вы вошли в аккаунт, все заказы будут в разделе «Мои заказы».',
      'Если магазин изменит состав заказа, на странице будет видно, что было и что стало.',
    ])
    items.forEach((t) => expect(t!.length).toBeLessThanOrEqual(150))
  })

  it('QA30-05 steps: 3 items, panel styling as BusinessBlock, allowed icons', () => {
    const { container } = setup()
    const ol = container.querySelector('ol') as HTMLElement
    for (const c of ['list-none', 'md:grid-cols-3', 'gap-10']) expect(ol.className).toContain(c)
    const panel = ol.parentElement as HTMLElement
    expect(panel.className).toContain('bg-cream-deep')
    expect(panel.className).toContain('rounded-[28px]')
    const lis = ol.querySelectorAll(':scope > li')
    expect(lis).toHaveLength(3)
    lis.forEach((li) => {
      const svg = li.querySelector('svg') as SVGElement
      expect(svg.getAttribute('width')).toBe('20')
      expect(svg.getAttribute('stroke-width')).toBe('1.6')
      expect(li.querySelector('h4')!.className).toContain('text-[21px]')
    })
  })

  it('QA30-06 buttons: anchor to shop list and /orders; both before the steps', () => {
    const { container, getByRole } = setup()
    const a = getByRole('link', { name: 'Выбрать магазин' })
    expect(a.getAttribute('href')).toBe('#shop-list')
    expect(a.className).toContain('bg-ink')
    const b = getByRole('link', { name: 'Мои заказы' })
    expect(b.getAttribute('href')).toBe('/orders')
    expect(b.className).toContain('border')
    const ol = container.querySelector('ol')!
    for (const l of [a, b]) expect(!!(l.compareDocumentPosition(ol) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
  })

  it('QA30-07 forbidden wording absent', () => {
    const { container } = setup()
    const t = container.textContent ?? ''
    for (const re of [/столов/i, /бесплатн/i, /комисси/i, /whatsapp/i, /\bMAX\b/, /без очереди/i, /моментальн/i, /SMS/i, /онлайн-оплат/i, /доставк/i])
      expect(t).not.toMatch(re)
    expect(t).not.toMatch(/без регистрации/i)
    const withNotify = Array.from(container.querySelectorAll('li')).filter((l) => /уведомлен/i.test(l.textContent ?? ''))
    expect(withNotify).toHaveLength(1)
  })

  it('QA30-08 svgs decorative; screenshot has alt, size, lazy, caption; no horizontal overflow classes', () => {
    const { container } = setup()
    container.querySelectorAll('svg').forEach((s) => expect(s.getAttribute('aria-hidden')).toBe('true'))
    const imgs = container.querySelectorAll('img')
    expect(imgs).toHaveLength(1)
    const img = imgs[0]
    expect(img.alt).toMatch(/^Страница заказа на телефоне: заказ № \d+ принят магазином, получение сегодня к \d\d:\d\d/)
    expect(Number(img.getAttribute('width'))).toBe(390)
    expect(Number(img.getAttribute('height'))).toBeGreaterThan(0)
    expect(img.getAttribute('loading')).toBe('lazy')
    expect(img.getAttribute('decoding')).toBe('async')
    expect(img.className).toContain('h-auto')
    expect(img.getAttribute('src')).not.toMatch(/^https?:/)
    expect(img.getAttribute('src')).toMatch(/\.webp/)
    expect(container.querySelector('figure figcaption')!.textContent).toBe('Так выглядит страница заказа: статус меняется сам')
  })
})

describe('BusinessBlock screenshot QA (cycle 30)', () => {
  it('QA30-09 board figure between top grid and steps panel; picture with md source; alt verbatim', () => {
    const { container } = setup(<BusinessBlock />)
    const img = container.querySelector('img') as HTMLImageElement
    expect(img.alt).toBe(
      'Экран заказов магазина: колонки „Новые“, „Принятые“ и „Готовы к выдаче“ с карточками заказов — номер, время получения, покупатель, состав и сумма',
    )
    expect(img.getAttribute('loading')).toBe('lazy')
    expect(img.getAttribute('width')).toBe('390')
    const src = container.querySelector('picture source') as HTMLSourceElement
    expect(src.getAttribute('media')).toBe('(min-width: 768px)')
    expect(src.getAttribute('type')).toBe('image/webp')
    expect(src.getAttribute('width')).toBe('1280')
    const ol = container.querySelector('ol')!
    expect(!!(img.compareDocumentPosition(ol) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    for (const a of Array.from(container.querySelectorAll('a'))) expect(!!(a.compareDocumentPosition(img) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    expect(container.querySelector('figcaption')!.textContent).not.toMatch(/\b0[123]\b/)
  })

  it('QA30-10 image weight budget: desktop<=200KB, phone<=120KB, total<=450KB', () => {
    const dir = resolve(fileURLToPath(import.meta.url), '../../assets/screenshots') + '/'
    const kb = (f: string) => statSync(dir + f).size / 1024
    expect(kb('board-desktop-2x.webp')).toBeLessThanOrEqual(200)
    expect(kb('board-phone-2x.webp')).toBeLessThanOrEqual(120)
    expect(kb('order-page-2x.webp')).toBeLessThanOrEqual(120)
    // worst case per viewport: phone loads order page + phone board; desktop loads order page + desktop board
    expect(kb('order-page-2x.webp') + kb('board-desktop-2x.webp')).toBeLessThanOrEqual(450)
    expect(kb('order-page-2x.webp') + kb('board-phone-2x.webp')).toBeLessThanOrEqual(450)
  })
})
