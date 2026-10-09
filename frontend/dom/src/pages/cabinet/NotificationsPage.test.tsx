import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, fireEvent, waitFor } from '@testing-library/react'
import { NotificationsPage } from './NotificationsPage'
import { companyFixture } from '../../test/fixtures'
import { renderCabinetPage } from '../../test/renderCabinet'
import type { StaysNotificationSettingsDto } from '../../types'

const api = vi.hoisted(() => ({ notificationSettings: vi.fn(), updateNotificationSettings: vi.fn() }))
vi.mock('../../api/staysCompanies', () => ({ staysCompaniesApi: api }))
// The numbers block (cycle 40) has its own API and its own tests; here it is a landmark of the shared block.
vi.mock('@/components/notifications/NumbersBlock', () => ({ NumbersBlock: () => <section data-testid="numbers-block">Номера</section> }))
vi.mock('@/api/notificationNumbers', () => ({
  NUMBERS_OVERVIEW_QUERY_KEY: ['notification-numbers'],
  notificationNumbersApi: {
    overview: () => Promise.resolve({
      transports: [
        { transport: 'Max', channel: { displayStatus: 'Working' } },
        { transport: 'WhatsApp', channel: { displayStatus: 'ActionRequired' } },
      ],
    }),
  },
}))

const settings = (over: Partial<StaysNotificationSettingsDto> = {}): StaysNotificationSettingsDto => ({
  staffPushEnabled: true, staffMaxEnabled: true, guestWebPushEnabled: true, guestMessengerEnabled: true, messengerAvailable: true,
  deliveryMode: 'PriorityChannel', priorityTransport: 'Max', messagingActive: true, deliveryChoiceVisible: false, priorityWarning: null, ...over,
} as StaysNotificationSettingsDto)

const open = () => renderCabinetPage(<NotificationsPage />, { company: companyFixture(), path: '/cabinet/:companyId/notifications', url: '/cabinet/co-1/notifications' })

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset())
  api.notificationSettings.mockResolvedValue(settings())
})

describe('NotificationsPage (dom) — the shared «Номера» block and the delivery choice (cycle 40)', () => {
  it('shows the shared numbers block and no delivery choice while the server says it is not worth showing', async () => {
    open()
    expect(await screen.findByTestId('numbers-block')).toBeInTheDocument()
    expect(screen.queryByText('Куда отправлять')).not.toBeInTheDocument()
  })

  it('offers the delivery choice when the server says so, and marks a priority that does not work', async () => {
    api.notificationSettings.mockResolvedValue(settings({
      deliveryChoiceVisible: true, priorityTransport: 'WhatsApp', priorityWarning: 'Приоритетный номер не работает: выберите другой или „во все“',
    }))
    open()
    expect(await screen.findByText('Куда отправлять')).toBeInTheDocument()
    expect(screen.getByTestId('priority-warning')).toHaveTextContent('Приоритетный номер не работает')
    expect(await screen.findByLabelText(/WhatsApp \(не работает\)/)).toBeChecked()
  })

  it('saves the flags as before', async () => {
    api.updateNotificationSettings.mockResolvedValue(settings({ staffPushEnabled: false }))
    open()
    fireEvent.click(await screen.findByRole('switch', { name: /Push о новых бронях/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.updateNotificationSettings).toHaveBeenCalledWith('co-1', expect.objectContaining({ staffPushEnabled: false, guestMessengerEnabled: true })))
  })
})
