import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { Footer } from './Footer'

// CY44-05: the footer shows the «Все продукты EZBOOK» link when enabled and keeps its legal links.
afterEach(() => vi.unstubAllEnvs())

describe('Footer products link', () => {
  it('flag on: link shown and the legal links stay', () => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_PRODUCTS_PAGE_LINK', 'on')
    render(
      <MemoryRouter>
        <Footer />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Все продукты EZBOOK' })).toHaveAttribute('href', 'https://about.ezbook.ru/')
    expect(screen.getByRole('link', { name: 'Политика обработки персональных данных' })).toHaveAttribute('href', '/privacy')
    expect(screen.getByRole('link', { name: 'Пользовательское соглашение' })).toHaveAttribute('href', '/terms')
  })

  it('flag off: no link', () => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_PRODUCTS_PAGE_LINK', '')
    render(
      <MemoryRouter>
        <Footer />
      </MemoryRouter>,
    )
    expect(screen.queryByTestId('products-page-link')).toBeNull()
    expect(screen.getByRole('link', { name: 'Политика обработки персональных данных' })).toBeInTheDocument()
  })
})
