import { describe, it, expect } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { MessengersBlock } from './MessengersBlock'
import type { OwnerMessengerLine, OwnerMessengersBlock } from '../../types'

const line = (o: Partial<OwnerMessengerLine> = {}): OwnerMessengerLine => ({
  transport: 'Max', label: 'MAX', status: 'NotConnected', statusText: 'Не подключён', pricePerMonth: 490, priceText: '490 ₽/мес', monthlyPrice: 0,
  paidUntil: null, isTrial: false, footnote: null, action: 'Connect', actionText: 'Подключить', targetCompanyId: 'c1', noCompanyText: null, ...o,
})
const block = (lines: OwnerMessengerLine[], o: Partial<OwnerMessengersBlock> = {}): OwnerMessengersBlock => ({
  title: 'Сообщения клиентам в мессенджеры', note: 'Общие для всех ваших компаний', lines, monthlyTotal: 0, monthlyTotalText: 'Мессенджеры: 0 ₽/мес',
  conditionsUrl: '/offer-channel', conditionsLabel: 'Условия', ...o,
})
const href = (id: string, t: string) => `/cabinet/${id}/notifications?connect=${t}`
const renderBlock = (b: OwnerMessengersBlock | null | undefined) =>
  render(<MemoryRouter><MessengersBlock block={b} notificationsHref={href} /></MemoryRouter>)

describe('MessengersBlock (cycle 43, US-43-05)', () => {
  it('renders nothing when the server sends no block or no lines', () => {
    expect(renderBlock(null).container).toBeEmptyDOMElement()
    expect(renderBlock(undefined).container).toBeEmptyDOMElement()
    expect(renderBlock(block([])).container).toBeEmptyDOMElement()
  })

  it('prints the server texts: title, note, status, price, footnote, total and the conditions link', () => {
    renderBlock(block([
      line({ transport: 'WhatsApp', label: 'WhatsApp', status: 'Paid', statusText: 'Оплачено до 20.11.2026', priceText: '490 ₽/мес', monthlyPrice: 490, footnote: 'Доступ к WhatsApp в России ограничен: сообщения могут не доходить', action: null, actionText: null }),
    ], { monthlyTotal: 490, monthlyTotalText: 'Мессенджеры: 490 ₽/мес' }))
    expect(screen.getByText('Сообщения клиентам в мессенджеры')).toBeInTheDocument()
    expect(screen.getByText('Общие для всех ваших компаний')).toBeInTheDocument()
    expect(screen.getByText('Оплачено до 20.11.2026')).toBeInTheDocument()
    expect(screen.getByText('Доступ к WhatsApp в России ограничен: сообщения могут не доходить')).toBeInTheDocument()
    expect(screen.getByText('Мессенджеры: 490 ₽/мес')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Условия' })).toHaveAttribute('href', '/offer-channel')
    expect(screen.queryByRole('link', { name: /Подключить|Продлить/ })).not.toBeInTheDocument()
  })

  it('the button is a link built by the site, with the transport in ?connect=', () => {
    renderBlock(block([line({ action: 'Renew', actionText: 'Продлить', status: 'Expired', statusText: 'Оплата закончилась 01.10.2026' })]))
    expect(screen.getByRole('link', { name: 'Продлить MAX' })).toHaveAttribute('href', '/cabinet/c1/notifications?connect=Max')
  })

  it('without a company of this line shows the server text instead of a dead button', () => {
    renderBlock(block([line({ targetCompanyId: null, noCompanyText: 'Сначала создайте компанию' })]))
    expect(screen.getByText('Сначала создайте компанию')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Подключить/ })).not.toBeInTheDocument()
  })

  it('a line with no price text (not sold, not paid) shows only the status', () => {
    renderBlock(block([line({ priceText: null, pricePerMonth: null, action: null, actionText: null })]))
    const row = screen.getByTestId('messenger-Max')
    expect(within(row).getByText('Не подключён')).toBeInTheDocument()
    expect(within(row).queryByText(/₽/)).not.toBeInTheDocument()
  })
})
