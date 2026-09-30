import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { BusinessBlock } from './BusinessBlock'
import manifest from '../assets/screenshots/screenshots.json'

const BOARD_ALT =
  'Экран заказов магазина: колонки „Новые“, „Принятые“ и „Готовы к выдаче“ с карточками заказов — номер, время получения, покупатель, состав и сумма'

describe('BusinessBlock screenshot', () => {
  it('T30-14 board figure: picture with desktop source, phone img, caption, position', () => {
    const { container } = render(<MemoryRouter><BusinessBlock /></MemoryRouter>)
    expect(container.querySelectorAll('figure')).toHaveLength(1)
    const img = container.querySelector('figure picture > img') as HTMLImageElement
    expect(img.getAttribute('alt')).toBe(BOARD_ALT)
    expect(img.getAttribute('width')).toBe(String(manifest.boardPhone.cssWidth))
    expect(img.getAttribute('height')).toBe(String(manifest.boardPhone.cssHeight))
    expect(img.getAttribute('loading')).toBe('lazy')
    expect(img.getAttribute('decoding')).toBe('async')
    const source = container.querySelector('picture > source[media="(min-width: 768px)"]') as HTMLSourceElement
    expect(source).not.toBeNull()
    const srcset = source.getAttribute('srcset') ?? ''
    expect(srcset).toContain(' 1x')
    expect(srcset).toContain(' 2x')
    expect(source.getAttribute('width')).toBe(String(manifest.boardDesktop.cssWidth))
    expect(source.getAttribute('height')).toBe(String(manifest.boardDesktop.cssHeight))
    expect(container.querySelector('figcaption')?.textContent).toBe('Экран заказов магазина: новые, принятые и готовые заказы')
    const figure = container.querySelector('figure') as Element
    const ul = container.querySelector('ul') as Element
    const ol = container.querySelector('ol') as Element
    expect(ul.compareDocumentPosition(figure) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(figure.compareDocumentPosition(ol) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
