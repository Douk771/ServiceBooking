import { describe, it, expect } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { CompanyCard, CompanyCardSkeleton, type CompanyCardData } from './CompanyCard'

const FULL: CompanyCardData = {
  name: 'Мясной двор',
  description: 'Свежее мясо каждый день',
  phone: '79991234567',
  email: 'shop@example.com',
  address: 'Ленина, 1',
  cityName: 'Барнаул',
  yandexMapsUrl: 'https://yandex.ru/maps/-/abc',
  twoGisUrl: 'https://2gis.ru/-/abc',
  photos: [],
}

const before = (a: Node, b: Node) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING)

describe('CompanyCard header (ARCHITECTURE_CYCLE31.md §31.11)', () => {
  it('DOM order is h1, description, slot, actions, meta', () => {
    render(<CompanyCard company={FULL}><span>Открыто</span></CompanyCard>)
    const h1 = screen.getByRole('heading', { level: 1 })
    const desc = screen.getByText('Свежее мясо каждый день')
    const slot = screen.getByTestId('company-card-slot')
    const actions = screen.getByTestId('company-card-actions')
    const meta = screen.getByTestId('company-card-meta')
    expect(before(h1, desc)).toBe(true)
    expect(before(desc, slot)).toBe(true)
    expect(before(slot, actions)).toBe(true)
    expect(before(actions, meta)).toBe(true)
    expect(within(slot).getByText('Открыто')).toBeInTheDocument()
  })

  it('a full card: tel link with its accessible name and both map links inside the actions group', () => {
    render(<CompanyCard company={FULL} />)
    const actions = screen.getByTestId('company-card-actions')
    const tel = within(actions).getByRole('link', { name: /Позвонить/ })
    expect(tel.getAttribute('href')).toMatch(/^tel:/)
    expect(within(actions).getByRole('link', { name: /Открыть в Яндекс Картах/ })).toHaveAttribute('href', FULL.yandexMapsUrl)
    expect(within(actions).getByRole('link', { name: /Открыть в 2ГИС/ })).toHaveAttribute('href', FULL.twoGisUrl)
    const meta = screen.getByTestId('company-card-meta')
    expect(within(meta).getByText(/Ленина, 1/)).toBeInTheDocument()
    expect(within(meta).getByRole('link', { name: /shop@example.com/ })).toHaveAttribute('href', 'mailto:shop@example.com')
  })

  it('a bare card has only the logo and the title: no empty wrappers', () => {
    render(<CompanyCard company={{ name: 'Салон', photos: [] }} />)
    expect(screen.getByRole('heading', { level: 1, name: 'Салон' })).toBeInTheDocument()
    expect(screen.queryByTestId('company-card-actions')).not.toBeInTheDocument()
    expect(screen.queryByTestId('company-card-meta')).not.toBeInTheDocument()
    expect(screen.queryByTestId('company-card-slot')).not.toBeInTheDocument()
    expect(document.querySelector('p')).toBeNull()
  })

  it('a whitespace-only description renders no paragraph', () => {
    render(<CompanyCard company={{ name: 'Салон', description: '   ', photos: [] }} />)
    expect(document.querySelector('p')).toBeNull()
  })

  it('children={false} (CompanyPage without a slot) creates no slot wrapper', () => {
    render(<CompanyCard company={FULL}>{false}</CompanyCard>)
    expect(screen.queryByTestId('company-card-slot')).not.toBeInTheDocument()
  })

  it('only a map link (no phone) still renders the actions group; only an address renders meta without actions', () => {
    const { unmount } = render(<CompanyCard company={{ name: 'A', yandexMapsUrl: 'https://yandex.ru/maps/-/x', photos: [] }} />)
    expect(screen.getByTestId('company-card-actions')).toBeInTheDocument()
    expect(screen.queryByTestId('company-card-meta')).not.toBeInTheDocument()
    unmount()
    render(<CompanyCard company={{ name: 'A', address: 'Ленина, 1', cityName: 'Барнаул', photos: [] }} />)
    expect(screen.queryByTestId('company-card-actions')).not.toBeInTheDocument()
    expect(screen.getByTestId('company-card-meta')).toBeInTheDocument()
  })

  it('the skeleton renders and is busy', () => {
    render(<CompanyCardSkeleton />)
    expect(screen.getByTestId('company-card-skeleton')).toHaveAttribute('aria-busy', 'true')
  })
})
