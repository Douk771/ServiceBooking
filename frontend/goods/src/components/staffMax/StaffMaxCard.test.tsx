import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StaffMaxCard } from './StaffMaxCard'
import type { StaffMaxStatusDto } from '../../types'

const status = vi.fn()
const createLinkSession = vi.fn()
const unlink = vi.fn()
vi.mock('../../api/staffMax', () => ({ staffMaxApi: { status: () => status(), createLinkSession: () => createLinkSession(), unlink: () => unlink() } }))

const dto = (over: Partial<StaffMaxStatusDto> = {}): StaffMaxStatusDto => ({
  available: true, canLink: true, unavailableText: null, eligible: true, status: 'NotLinked', statusText: 'Не подключено',
  linkedAtUtc: null, stoppedAtUtc: null, pendingSession: null, shops: [{ shopId: 's1', name: 'Шаурма', staffMaxEnabled: true }], pollIntervalSeconds: 2, ...over,
})
const renderCard = () => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><StaffMaxCard /></QueryClientProvider>)

beforeEach(() => {
  status.mockReset()
  createLinkSession.mockReset()
  unlink.mockReset()
})

describe('StaffMaxCard', () => {
  it('renders nothing for someone who is not a shop member', async () => {
    status.mockResolvedValue(dto({ eligible: false }))
    const { container } = renderCard()
    await waitFor(() => expect(status).toHaveBeenCalled())
    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })

  it('shows the fixed platform-off text and disables the connect button', async () => {
    status.mockResolvedValue(dto({ available: false, canLink: false, unavailableText: 'Сообщения в MAX пока не включены на платформе' }))
    renderCard()
    expect(await screen.findByTestId('max-unavailable')).toHaveTextContent('пока не включены на платформе')
    expect(screen.getByRole('button', { name: 'Подключить MAX' })).toBeDisabled()
  })

  it('gets a link, shows it and keeps polling only while pending', async () => {
    status.mockResolvedValueOnce(dto())
    createLinkSession.mockResolvedValue({ sessionId: 'x', deepLink: 'https://max.ru/bot?start=sm1.a', webLink: 'https://web.max.ru/bot?start=sm1.a', qrPngBase64: null, expiresAtUtc: new Date(Date.now() + 600_000).toISOString(), ttlSeconds: 600, pollIntervalSeconds: 2 })
    status.mockResolvedValue(dto({ status: 'Pending', statusText: 'Ждём подтверждения в MAX…' }))
    const user = userEvent.setup()
    renderCard()
    await user.click(await screen.findByRole('button', { name: 'Подключить MAX' }))
    expect(await screen.findByDisplayValue('https://max.ru/bot?start=sm1.a')).toBeInTheDocument()
    expect(screen.getByTestId('max-status')).toHaveTextContent('Ждём подтверждения в MAX…')
    expect(screen.getByRole('link', { name: /веб-версии MAX/ })).toHaveAttribute('href', 'https://web.max.ru/bot?start=sm1.a')
  })

  it('says the link is stale once the pending session has expired', async () => {
    status.mockResolvedValue(dto({ status: 'Pending', statusText: 'Ждём подтверждения в MAX…', pendingSession: { sessionId: 'x', expiresAtUtc: new Date(Date.now() - 1000).toISOString() } }))
    renderCard()
    expect(await screen.findByText('Ссылка устарела')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Получить новую/ })).toBeEnabled()
  })

  it('shows the server text when the link is refused and lets a linked user disconnect', async () => {
    status.mockResolvedValue(dto({ status: 'Linked', statusText: 'Подключено 30.09.2026' }))
    unlink.mockResolvedValue(undefined)
    createLinkSession.mockRejectedValue({ response: { status: 409, data: 'Подключение к MAX временно недоступно, попробуйте позже' } })
    const user = userEvent.setup()
    renderCard()
    expect(await screen.findByText('Подключено 30.09.2026')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Подключить другой чат' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('временно недоступно')
    await user.click(screen.getByRole('button', { name: 'Отключить' }))
    await waitFor(() => expect(unlink).toHaveBeenCalledTimes(1))
  })
})
