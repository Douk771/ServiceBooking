import { afterEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { fillOperatorTemplate, OPERATOR, telHref } from '@/utils/operatorRequisites'
import { STOREFRONT_PAGES } from '@/utils/storefrontMarkup'
import { DomFooter } from './DomFooter'

// CY44-05: the footer shows the «Все продукты EZBOOK» link when enabled and keeps its legal links.
afterEach(() => vi.unstubAllEnvs())

describe('DomFooter products link', () => {
  it('flag on: link shown and the legal links stay', () => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_PRODUCTS_PAGE_LINK', 'on')
    render(
      <MemoryRouter>
        <DomFooter />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Все продукты EZBOOK' })).toHaveAttribute(
      'href',
      'https://about.ezbook.ru/',
    )
    expect(screen.getByRole('link', { name: 'Политика обработки персональных данных' })).toHaveAttribute(
      'href',
      '/privacy',
    )
    expect(screen.getByRole('link', { name: 'Пользовательское соглашение' })).toHaveAttribute('href', '/terms')
  })

  it('flag off: no link', () => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('VITE_PRODUCTS_PAGE_LINK', '')
    render(
      <MemoryRouter>
        <DomFooter />
      </MemoryRouter>,
    )
    expect(screen.queryByTestId('products-page-link')).toBeNull()
    expect(screen.getByRole('link', { name: 'Политика обработки персональных данных' })).toBeInTheDocument()
  })
})

// CY48-01 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1): the requisites lines in the footer; the previous links stay.
describe('DomFooter requisites (CY48-01)', () => {
  const PREVIOUS_LINKS = [
    ['Политика обработки персональных данных', '/privacy'],
    ['Пользовательское соглашение', '/terms'],
    ['Соглашение с компанией', '/terms-owner'],
    ['Согласие на обработку ПДн', '/pdn-consent'],
    ['Обращение по своим данным', '/data-request'],
  ]
  const renderFooter = () =>
    render(
      <MemoryRouter>
        <DomFooter />
      </MemoryRouter>,
    )

  it('the identity line is the filled template; INN and phone do not break', () => {
    renderFooter()
    const box = screen.getByTestId('operator-requisites')
    const paragraphs = box.querySelectorAll('p')
    expect(paragraphs).toHaveLength(2)
    expect(paragraphs[0].textContent).toBe(fillOperatorTemplate(STOREFRONT_PAGES.footer.identity))
    const inn = screen.getByText(OPERATOR['ИНН_ОПЕРАТОРА'])
    expect(inn.className).toContain('whitespace-nowrap')
    expect(screen.getByText(OPERATOR['ТЕЛЕФОН_ОПЕРАТОРА']).className).toContain('whitespace-nowrap')
  })

  it('phone, support mail, contacts and payment pages: right hrefs, tap target >= 24 px', () => {
    renderFooter()
    const phone = screen.getByRole('link', { name: OPERATOR['ТЕЛЕФОН_ОПЕРАТОРА'] })
    expect(phone).toHaveAttribute('href', telHref())
    expect(telHref()).toMatch(/^tel:\+7\d{10}$/)
    expect(screen.getByRole('link', { name: OPERATOR['ПОЧТА_ПОДДЕРЖКИ'] })).toHaveAttribute(
      'href',
      `mailto:${OPERATOR['ПОЧТА_ПОДДЕРЖКИ']}`,
    )
    const contacts = screen.getByRole('link', { name: 'Контакты и реквизиты' })
    const payment = screen.getByRole('link', { name: 'Оплата и возврат' })
    expect(contacts).toHaveAttribute('href', '/contacts')
    expect(payment).toHaveAttribute('href', '/payment-info')
    for (const a of [phone, contacts, payment]) expect(a.className).toContain('min-h-[24px]')
  })

  it('the previous links are unchanged (text and href)', () => {
    renderFooter()
    for (const [name, href] of PREVIOUS_LINKS)
      expect(screen.getByRole('link', { name }), name).toHaveAttribute('href', href)
  })

  it('footer text is not the low-contrast text-muted', () => {
    const { container } = renderFooter()
    expect(container.querySelector('.text-muted')).toBeNull()
    expect(container.querySelector('footer > div')?.className).toContain('text-ink-soft')
  })
})
