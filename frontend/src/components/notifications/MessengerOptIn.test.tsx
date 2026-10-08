import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MessengerOptIn } from './MessengerOptIn'
import { StaffMessengerConsentCheckbox } from './StaffMessengerConsentCheckbox'
import type { MessengerOptInState } from '../../hooks/useMessengerOptInDefault'

const apiGet = vi.fn()
vi.mock('../../api/client', () => ({ api: { get: (...a: unknown[]) => apiGet(...a) } }))
const notFound = () => Promise.reject(Object.assign(new Error('404'), { response: { status: 404 } }))

const offer = { offered: true, transports: ['Max' as const], checkboxLabel: 'Получать уведомления о записи в MAX' }

function state(over: Partial<MessengerOptInState> = {}): MessengerOptInState {
  return {
    initial: { kind: 'guest', checked: false },
    checked: false,
    setChecked: vi.fn(),
    optedOut: false,
    loading: false,
    payload: () => undefined,
    ...over,
  }
}

function wrap(ui: React.ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>)
}

describe('MessengerOptIn', () => {
  it('renders nothing when the server does not offer messages, and does not fetch the legal text', () => {
    apiGet.mockReset().mockImplementation(notFound)
    const { container } = wrap(<MessengerOptIn kind="booking" offer={{ offered: false, transports: [], checkboxLabel: null }} state={state()} profileHref="/profile" />)
    expect(container).toBeEmptyDOMElement()
    expect(apiGet).not.toHaveBeenCalled()
  })

  it('shows the server label and the verbatim fallback when the lawyer text is not published (404)', async () => {
    apiGet.mockReset().mockImplementation(notFound)
    wrap(<MessengerOptIn kind="booking" offer={offer} state={state()} profileHref="/profile" />)
    expect(screen.getByRole('checkbox', { name: 'Получать уведомления о записи в MAX' })).not.toBeChecked()
    expect(await screen.findByText(/передаются ООО «ГРИН-АПИ»/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Текст согласия' })).toHaveAttribute('href', '/pdn-consent')
  })

  it('expands the full fallback text behind «Подробнее»', async () => {
    apiGet.mockReset().mockImplementation(notFound)
    const user = userEvent.setup()
    wrap(<MessengerOptIn kind="booking" offer={offer} state={state()} companyName="Барбершоп «Гвоздь»" profileHref="/profile" />)
    const more = await screen.findByRole('button', { name: 'Подробнее' })
    expect(screen.queryByText(/Что вы разрешаете/)).toBeNull()
    await user.click(more)
    expect(screen.getByText(/Что вы разрешаете/)).toBeInTheDocument()
    expect(screen.getByText(/компании «Барбершоп «Гвоздь»»/)).toBeInTheDocument()
  })

  it('uses the lawyer text sections when the key is published', async () => {
    apiGet.mockReset().mockResolvedValue({
      data: { version: 'v1', contentHtml: '<h2>Короткая строка</h2><p>Текст юриста коротко.</p><h2>Полный текст</h2><p>Текст юриста полностью.</p>' },
    })
    wrap(<MessengerOptIn kind="order" offer={offer} state={state()} profileHref="/profile" />)
    expect(await screen.findByText('Текст юриста коротко.')).toBeInTheDocument()
    expect(apiGet).toHaveBeenCalledWith('/legal/texts/OrderMessengerConsent')
  })

  it('opted out: a line with a link to the profile instead of the box', () => {
    apiGet.mockReset().mockImplementation(notFound)
    wrap(<MessengerOptIn kind="stay" offer={offer} state={state({ optedOut: true })} profileHref="https://ezbook.ru/profile" />)
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.getByText(/Уведомления в мессенджеры выключены в профиле/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Изменить' })).toHaveAttribute('href', 'https://ezbook.ru/profile')
  })

  it('opted out without a known profile address keeps the line and drops the link', () => {
    apiGet.mockReset().mockImplementation(notFound)
    wrap(<MessengerOptIn kind="stay" offer={offer} state={state({ optedOut: true })} profileHref={null} />)
    expect(screen.getByText(/выключены в профиле/)).toBeInTheDocument()
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('reports the click to the state', async () => {
    apiGet.mockReset().mockImplementation(notFound)
    const setChecked = vi.fn()
    const user = userEvent.setup()
    wrap(<MessengerOptIn kind="booking" offer={offer} state={state({ setChecked })} profileHref="/profile" />)
    await user.click(screen.getByRole('checkbox'))
    expect(setChecked).toHaveBeenCalledWith(true)
  })
})

describe('StaffMessengerConsentCheckbox', () => {
  it('renders nothing when not offered', () => {
    apiGet.mockReset().mockImplementation(notFound)
    const { container } = wrap(<StaffMessengerConsentCheckbox offer={{ offered: false, transports: [] }} checked={false} onChange={() => {}} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the verbatim label with the messenger and the §7.3 hint as the description', async () => {
    apiGet.mockReset().mockImplementation(notFound)
    wrap(<StaffMessengerConsentCheckbox offer={{ offered: true, transports: ['WhatsApp', 'Max'] }} checked={false} onChange={() => {}} />)
    const box = screen.getByRole('checkbox', { name: 'Клиент согласился получать сообщения об этой записи в WhatsApp и MAX' })
    expect(box).not.toBeChecked()
    expect(await screen.findByText(/Отмечайте, только если клиент прямо согласился/)).toBeInTheDocument()
    expect(box).toHaveAccessibleDescription(/Отмечайте, только если клиент прямо согласился/)
  })

  it('takes the hint from the StaffBookingMessengerConsentHint key when published', async () => {
    apiGet.mockReset().mockResolvedValue({ data: { version: 'v1', contentHtml: '<p>Подсказка юриста.</p>' } })
    wrap(<StaffMessengerConsentCheckbox offer={{ offered: true, transports: ['Max'] }} checked={false} onChange={() => {}} />)
    expect(await screen.findByText('Подсказка юриста.')).toBeInTheDocument()
    expect(apiGet).toHaveBeenCalledWith('/legal/texts/StaffBookingMessengerConsentHint')
  })
})
