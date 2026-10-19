import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ProductsPageLink } from './ProductsPageLink'

// CY44-05 (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.8): the rule of showing the footer link.
afterEach(() => vi.unstubAllEnvs())

describe('CY44-05 ProductsPageLink', () => {
  it('dev: link to the local about port', () => {
    vi.stubEnv('DEV', true)
    render(<ProductsPageLink />)
    expect(screen.getByTestId('products-page-link')).toHaveAttribute('href', 'http://localhost:5177/')
  })

  it('release without the flag: no link', () => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_PRODUCTS_PAGE_LINK', '')
    const { container } = render(<ProductsPageLink />)
    expect(container).toBeEmptyDOMElement()
  })

  it('release with the flag: production address, same tab', () => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_PRODUCTS_PAGE_LINK', 'on')
    render(<ProductsPageLink />)
    const a = screen.getByRole('link', { name: 'Все продукты EZBOOK' })
    expect(a).toHaveAttribute('href', 'https://about.ezbook.ru/')
    expect(a).not.toHaveAttribute('target')
  })

  it('a broken VITE_SITE_URL_ABOUT hides the link instead of failing', () => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_PRODUCTS_PAGE_LINK', 'on')
    vi.stubEnv('VITE_SITE_URL_ABOUT', 'about.ezbook.ru/')
    const { container } = render(<ProductsPageLink />)
    expect(container).toBeEmptyDOMElement()
  })
})
