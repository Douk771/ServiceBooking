import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { CatalogCard } from './CatalogCard'

const r = (p: Partial<React.ComponentProps<typeof CatalogCard>> = {}) =>
  render(<MemoryRouter><CatalogCard to="/s/x" name="Имя" {...p} /></MemoryRouter>)

describe('CatalogCard (T38-07)', () => {
  it('prints 200-char word and 300-char sentence in full, without truncation classes', () => {
    const word = 'Я'.repeat(200)
    const addr = 'улица '.repeat(51).trim()
    r({ name: word, place: addr })
    const h3 = screen.getByRole('heading', { level: 3 })
    expect(h3.textContent).toBe(word)
    expect(h3.className).toContain('break-words')
    expect(h3.className).not.toMatch(/truncate|line-clamp/)
    const place = screen.getByText(addr)
    expect(place.className).toContain('break-words')
    expect(place.className).not.toMatch(/truncate|line-clamp/)
    expect(addr.length).toBeGreaterThanOrEqual(300)
  })
  it('passes aria-label and data-testid to the link', () => {
    r({ ariaLabel: 'Магазин, открыто, принимает', testId: 'catalog-shop' })
    const a = screen.getByTestId('catalog-shop')
    expect(a).toHaveAttribute('aria-label', 'Магазин, открыто, принимает')
    expect(a).toHaveAttribute('href', '/s/x')
  })
  it.each([
    ['success', 'bg-success-bg'],
    ['info', 'bg-info-bg'],
    ['muted', 'bg-cream-deep'],
  ] as const)('pill tone %s', (tone, cls) => {
    r({ pill: { text: 'Плашка', tone } })
    expect(screen.getByText('Плашка').className).toContain(cls)
  })
  it('renders subtitle, badge, description', () => {
    r({ subtitle: 'Открыто', badge: <i>бейдж</i>, description: 'Описание' })
    expect(screen.getByText('Открыто')).toBeInTheDocument()
    expect(screen.getByText('бейдж')).toBeInTheDocument()
    expect(screen.getByText('Описание').className).toContain('line-clamp-2')
  })
})
