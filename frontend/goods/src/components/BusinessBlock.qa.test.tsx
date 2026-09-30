import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { BusinessBlock } from './BusinessBlock'
import { useAuthStore } from '@/store/authStore'

// QA-тесты цикла 27, написаны по SPEC.md (US-27-01..05), независимо от реализации.
const setup = () => render(<MemoryRouter><BusinessBlock /></MemoryRouter>)
beforeEach(() => useAuthStore.setState({ user: null, token: null }))
afterEach(() => useAuthStore.setState({ user: null, token: null }))

describe('BusinessBlock QA (cycle 27)', () => {
  it('QA27-01 eyebrow keeps the old style', () => {
    const { getByText } = setup()
    const cls = getByText('Для бизнеса').className
    expect(cls).toContain('text-[13px]')
    expect(cls).toContain('uppercase')
    expect(cls).toContain('text-gold-dark')
  })

  it('QA27-02 step texts verbatim', () => {
    const { container } = setup()
    const texts = Array.from(container.querySelectorAll('ol > li p')).map((p) => p.textContent)
    expect(texts).toEqual([
      'Добавьте категории и товары с фото — поштучно или на вес, с ценой за штуку или за килограмм.',
      'Повесьте QR-код у кассы, отправьте ссылку постоянным покупателям. Заказ оформляется с телефона на одном экране.',
      'Новый заказ приходит на экран со звуком. Примите, соберите и выдайте его с точной суммой.',
    ])
    texts.forEach((t) => expect((t ?? '').length).toBeLessThanOrEqual(140))
  })

  it('QA27-03 no numbering and no ladder offsets', () => {
    const { container } = setup()
    const text = container.textContent ?? ''
    expect(text).not.toMatch(/\b0[123]\b/)
    expect(container.innerHTML).not.toMatch(/md:ml-(10|20)/)
  })

  it('QA27-04 step card styling as on ezbook', () => {
    const { container } = setup()
    const ol = container.querySelector('ol') as HTMLElement
    expect(ol.className).toContain('list-none')
    expect(ol.className).toContain('md:grid-cols-3')
    expect(ol.className).toContain('gap-10')
    const lis = ol.querySelectorAll(':scope > li')
    expect(lis).toHaveLength(3)
    lis.forEach((li) => {
      const circle = li.querySelector('div') as HTMLElement
      for (const c of ['w-[46px]', 'h-[46px]', 'rounded-full', 'bg-cream', 'border-line-strong']) expect(circle.className).toContain(c)
      const svg = circle.querySelector('svg') as SVGElement
      expect(svg.getAttribute('width')).toBe('20')
      expect(svg.getAttribute('stroke-width')).toBe('1.6')
      const h = li.querySelector('h4') as HTMLElement
      expect(h.className).toContain('font-serif')
      expect(h.className).toContain('text-[21px]')
      expect((li.querySelector('p') as HTMLElement).className).toContain('text-ink-soft')
    })
  })

  it('QA27-05 steps sit on a cream-deep rounded panel', () => {
    const { container } = setup()
    const panel = (container.querySelector('ol') as HTMLElement).parentElement as HTMLElement
    expect(panel.className).toContain('bg-cream-deep')
    expect(panel.className).toMatch(/rounded-\[2\d+px\]/)
  })

  it('QA27-06 buttons come before the steps in the document', () => {
    const { container } = setup()
    const links = Array.from(container.querySelectorAll('a'))
    const ol = container.querySelector('ol') as HTMLElement
    expect(links).toHaveLength(2)
    links.forEach((a) => expect(a.compareDocumentPosition(ol) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy())
  })

  it('QA27-07 button styles: primary dark with arrow, secondary white with border', () => {
    const { container } = setup()
    const [primary, secondary] = Array.from(container.querySelectorAll('a'))
    expect(primary.className).toContain('bg-ink')
    expect(primary.querySelector('svg')).toBeTruthy()
    expect(secondary.className).toContain('bg-white')
    expect(secondary.className).toContain('border')
    expect(primary.parentElement?.className).toContain('flex-wrap')
  })

  it('QA27-08 heading hierarchy h2 -> h3 -> h4, one h2, benefits check icons', () => {
    const { container } = setup()
    expect(container.querySelectorAll('h2')).toHaveLength(1)
    expect(container.querySelectorAll('h3')).toHaveLength(1)
    expect(container.querySelectorAll('h4')).toHaveLength(3)
    const items = container.querySelectorAll('ul > li')
    expect(items).toHaveLength(4)
    items.forEach((li) => expect(li.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true'))
  })

  it('QA27-09 mentions chats/one place', () => {
    const { container } = setup()
    expect(container.textContent).toMatch(/по чатам/)
    expect(container.textContent).toMatch(/в одном месте/)
  })
})
