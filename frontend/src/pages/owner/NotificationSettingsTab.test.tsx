import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NotificationSettingsTab } from './NotificationSettingsTab'
import type { NotificationSettings } from '../../types'

const getSettings = vi.fn()
const updateSettings = vi.fn()

vi.mock('../../api/notifications', () => ({
  notificationsApi: {
    getSettings: (...args: unknown[]) => getSettings(...args),
    updateSettings: (...args: unknown[]) => updateSettings(...args),
  },
}))

// The numbers block has its own API and its own tests (NumbersBlock.test.tsx); here it is only a landmark.
vi.mock('../../components/notifications/NumbersBlock', () => ({ NumbersBlock: () => <div data-testid="numbers-block" /> }))
vi.mock('../../components/push/StaffPushSettingsCard', () => ({ StaffPushSettingsCard: () => <div data-testid="staff-push" /> }))

function settings(overrides: Partial<NotificationSettings> = {}): NotificationSettings {
  return {
    enabledTypes: ['BookingConfirmed', 'Reminder', 'BookingCancelled', 'BookingRescheduled'],
    reminderLeadMinutes: 1440,
    minLeadMinutes: 120,
    planAllowsChannel: true,
    channel: { assigned: true, channelId: 'ch1', state: 'Connected', paymentState: 'Paid', paidUntil: null },
    effectiveEnabled: true,
    blockedReason: null,
    deliveryMode: 'PriorityChannel',
    priorityTransport: 'Max',
    connectedTransports: ['Max'],
    priorityChannelHealthy: true,
    messagingActive: true,
    inactiveText: null,
    deliveryChoiceVisible: false,
    priorityWarning: null,
    workingTransports: ['Max'],
    ...overrides,
  }
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <NotificationSettingsTab companyId="c1" />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getSettings.mockReset()
  updateSettings.mockReset()
})

describe('NotificationSettingsTab — one section «Уведомления клиентам» (US-40-06)', () => {
  it('shows the numbers block first, then the types, then the staff card — and no tariff or channel stubs', async () => {
    getSettings.mockResolvedValue(settings())
    renderTab()

    expect(await screen.findByText('Какие сообщения отправлять')).toBeInTheDocument()
    const order = [screen.getByTestId('numbers-block'), screen.getByText('Какие сообщения отправлять'), screen.getByTestId('staff-push')]
    for (let i = 1; i < order.length; i++)
      expect(order[i - 1].compareDocumentPosition(order[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.queryByText(/более высоком тарифе/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Уведомления → Каналы/)).not.toBeInTheDocument()
  })

  it('explains an inactive company but still lets the owner save in advance', async () => {
    const user = userEvent.setup()
    getSettings.mockResolvedValue(settings({ messagingActive: false, inactiveText: 'Подключите MAX выше', workingTransports: [] }))
    renderTab()

    expect(await screen.findByText(/Подключите MAX выше/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1))
  })
})

describe('NotificationSettingsTab — delivery choice (US-40-09)', () => {
  it('has no delivery block while the server says the choice is not worth showing (one working messenger)', async () => {
    getSettings.mockResolvedValue(settings())
    renderTab()

    await screen.findByText('Какие сообщения отправлять')
    expect(screen.queryByText('Как доставлять клиенту')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Приоритетный канал')).not.toBeInTheDocument()
  })

  it('offers the mode picker when the server says so, limiting the priority select to the working messengers', async () => {
    const user = userEvent.setup()
    getSettings.mockResolvedValue(settings({ deliveryChoiceVisible: true, workingTransports: ['WhatsApp', 'Max'], priorityTransport: 'WhatsApp' }))
    renderTab()

    const prioritySelect = await screen.findByLabelText('Приоритетный канал')
    const options = Array.from(prioritySelect.querySelectorAll('option')).map((o) => o.textContent)
    expect(options).toEqual(['WhatsApp', 'MAX'])

    await user.selectOptions(prioritySelect, 'Max')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    // §114.4 — only priorityTransport was touched; deliveryMode is omitted rather than re-sent ("не прислали — не меняем").
    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith('c1', expect.objectContaining({ priorityTransport: 'Max' })))
    expect(updateSettings.mock.calls[0][1]).not.toHaveProperty('deliveryMode')
  })

  it('keeps a priority that stopped working visible, marked, with the server warning — no silent fallback to the other messenger', async () => {
    getSettings.mockResolvedValue(
      settings({
        deliveryChoiceVisible: true, workingTransports: ['Max'], priorityTransport: 'WhatsApp',
        priorityWarning: 'Приоритетный номер не работает: выберите другой или „во все“',
      }),
    )
    renderTab()

    expect(await screen.findByText(/Приоритетный номер не работает/)).toBeInTheDocument()
    const prioritySelect = screen.getByLabelText('Приоритетный канал')
    expect(prioritySelect).toHaveValue('WhatsApp')
    const options = Array.from(prioritySelect.querySelectorAll('option')).map((o) => o.textContent)
    expect(options).toEqual(['MAX', 'WhatsApp (не работает)'])
  })

  it('omits deliveryMode and priorityTransport entirely when the owner only changes an unrelated field (§114.4)', async () => {
    const user = userEvent.setup()
    getSettings.mockResolvedValue(settings({ deliveryChoiceVisible: true, workingTransports: ['WhatsApp', 'Max'] }))
    renderTab()

    await screen.findByLabelText('Приоритетный канал')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1))
    const payload = updateSettings.mock.calls[0][1]
    expect(payload).not.toHaveProperty('deliveryMode')
    expect(payload).not.toHaveProperty('priorityTransport')
  })

  it('switching to "all messengers" warns about duplicate messages and saves the mode', async () => {
    const user = userEvent.setup()
    getSettings.mockResolvedValue(settings({ deliveryChoiceVisible: true, workingTransports: ['WhatsApp', 'Max'] }))
    renderTab()

    await user.click(await screen.findByRole('radio', { name: 'Во все подключённые мессенджеры' }))
    expect(screen.getByText(/клиент получит два одинаковых сообщения/i)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith('c1', expect.objectContaining({ deliveryMode: 'AllChannels' })))
  })
})
