import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { ShopNotificationsPage } from './ShopNotificationsPage'
import type { ShopManageDto, ShopNotificationSettingsDto } from '../../types'

const get = vi.fn()
const put = vi.fn()
vi.mock('../../api/shopNotifications', () => ({ shopNotificationsApi: { get: (...a: unknown[]) => get(...a), put: (...a: unknown[]) => put(...a) } }))
// The numbers block (cycle 40) has its own API and its own tests; here it is a landmark of the shared block.
vi.mock('@/components/notifications/NumbersBlock', () => ({ NumbersBlock: () => <section data-testid="numbers-block">Номера</section> }))

const settings = (over: Partial<ShopNotificationSettingsDto> = {}): ShopNotificationSettingsDto => ({
  staffPushEnabled: true, customerWebPushEnabled: true, customerMessengerEnabled: false, deliveryMode: 'PriorityChannel', priorityTransport: 'Max',
  messengerAvailable: false, messengerUnavailableText: 'Подключите номер для сообщений покупателям', platformPushEnabled: false, channels: [], staffMaxEnabled: true, staffMaxAvailable: true, ...over,
})

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/s1/notifications']}>
        <Routes>
          <Route element={<Outlet context={{ shop: { id: 's1', name: 'Шаурма' } as ShopManageDto, isOwner: true }} />}>
            <Route path="/cabinet/:shopId/notifications" element={<ShopNotificationsPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  get.mockReset().mockResolvedValue(settings())
  put.mockReset()
})

describe('ShopNotificationsPage', () => {
  it('disables the messenger switch and prints the server reason when no paid number is assigned', async () => {
    renderPage()
    expect(await screen.findByTestId('messenger-unavailable')).toHaveTextContent('Подключите номер для сообщений покупателям')
    expect(screen.getByRole('switch', { name: /Сообщения в MAX\/WhatsApp/ })).toBeDisabled()
  })

  it('says the platform has browser push switched off', async () => {
    renderPage()
    expect((await screen.findAllByText(/пока не включены на платформе/)).length).toBeGreaterThan(0)
  })

  it('saves the whole set of flags and shows the confirmation', async () => {
    put.mockResolvedValue(settings({ staffPushEnabled: false }))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('switch', { name: /Push о новых заказах/ }))
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(put).toHaveBeenCalledWith('s1', { staffPushEnabled: false, customerWebPushEnabled: true, customerMessengerEnabled: false, staffMaxEnabled: true, deliveryMode: 'PriorityChannel', priorityTransport: 'Max' }))
    expect(await screen.findByText('Сохранено')).toBeInTheDocument()
  })

  const connectedMax = { channelId: 'c1', transport: 'Max' as const, phoneMasked: '+7 (9**) ***-**-67', stateText: 'Подключён', isConnected: true, funded: true, fundingText: 'Оплачен до 31.10.2026' }
  const connectedWhatsApp = { ...connectedMax, channelId: 'c2', transport: 'WhatsApp' as const }

  it('has no delivery mode while the server says the choice is not worth showing (one working messenger)', async () => {
    get.mockResolvedValue(settings({ messengerAvailable: true, messengerUnavailableText: null, customerMessengerEnabled: true, deliveryChoiceVisible: false, channels: [connectedMax] }))
    renderPage()
    await screen.findByRole('switch', { name: /Сообщения в MAX\/WhatsApp/ })
    expect(screen.queryByRole('radiogroup', { name: 'Режим доставки' })).not.toBeInTheDocument()
  })

  it('offers delivery mode when the server says so (two working messengers) and warns about a broken priority', async () => {
    get.mockResolvedValue(settings({
      messengerAvailable: true, messengerUnavailableText: null, customerMessengerEnabled: true, deliveryChoiceVisible: true, priorityTransport: 'WhatsApp',
      priorityWarning: 'Приоритетный номер не работает: выберите другой или „во все“', channels: [connectedMax, { ...connectedWhatsApp, isConnected: false }],
    }))
    renderPage()
    expect(await screen.findByRole('radiogroup', { name: 'Режим доставки' })).toBeInTheDocument()
    expect(screen.getByTestId('priority-warning')).toHaveTextContent('Приоритетный номер не работает')
    expect(screen.getByRole('radio', { name: /WhatsApp \(не работает\)/ })).toBeInTheDocument()
  })

  it('shows the shared numbers block instead of the old channel cards', async () => {
    renderPage()
    expect(await screen.findByTestId('numbers-block')).toBeInTheDocument()
  })

  it('shows the server 409 text when messages were refused', async () => {
    get.mockResolvedValue(settings({ messengerAvailable: true, messengerUnavailableText: null }))
    put.mockImplementation(() => Promise.reject({ response: { status: 409, data: { code: 'MessengerUnavailable', message: 'Сначала подключите и оплатите номер для сообщений покупателям' } } }))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('switch', { name: /Сообщения в MAX\/WhatsApp/ }))
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(await screen.findByText('Сначала подключите и оплатите номер для сообщений покупателям')).toBeInTheDocument()
  })

  it('has an error state with a retry when the settings cannot be loaded', async () => {
    get.mockImplementation(() => Promise.reject({ response: { status: 500, data: '' } }))
    renderPage()
    expect(await screen.findByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
