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
    priorityTransport: 'WhatsApp',
    connectedTransports: ['WhatsApp'],
    priorityChannelHealthy: true,
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

describe('NotificationSettingsTab — delivery mode (US-125)', () => {
  it('explains that the mode has no effect yet when only one transport is connected, and hides the picker', async () => {
    getSettings.mockResolvedValue(settings())
    renderTab()

    expect(await screen.findByText(/выбор режима пока ни на что не влияет/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Приоритетный канал')).not.toBeInTheDocument()
  })

  it('offers the mode picker once two transports are connected, restricting the priority select to connected transports', async () => {
    const user = userEvent.setup()
    getSettings.mockResolvedValue(settings({ connectedTransports: ['WhatsApp', 'Max'] }))
    renderTab()

    const prioritySelect = await screen.findByLabelText('Приоритетный канал')
    const options = Array.from(prioritySelect.querySelectorAll('option')).map((o) => o.textContent)
    expect(options).toEqual(['WhatsApp', 'MAX'])

    await user.selectOptions(prioritySelect, 'Max')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() =>
      expect(updateSettings).toHaveBeenCalledWith(
        'c1',
        expect.objectContaining({ deliveryMode: 'PriorityChannel', priorityTransport: 'Max' }),
      ),
    )
  })

  it('shows the "priority channel unavailable" banner without silently switching transport', async () => {
    getSettings.mockResolvedValue(
      settings({ connectedTransports: ['WhatsApp', 'Max'], priorityChannelHealthy: false }),
    )
    renderTab()

    expect(await screen.findByText(/Приоритетный канал не работает/)).toBeInTheDocument()
    // Still shows WhatsApp as selected — no automatic fallback to another transport (§104.5).
    expect(screen.getByLabelText('Приоритетный канал')).toHaveValue('WhatsApp')
  })

  it('switching to "all channels" warns about duplicate messages and saves the mode', async () => {
    const user = userEvent.setup()
    getSettings.mockResolvedValue(settings({ connectedTransports: ['WhatsApp', 'Max'] }))
    renderTab()

    await user.click(await screen.findByRole('radio', { name: 'Во все подключённые каналы' }))
    expect(screen.getByText(/клиент получит два одинаковых сообщения/i)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() =>
      expect(updateSettings).toHaveBeenCalledWith('c1', expect.objectContaining({ deliveryMode: 'AllChannels' })),
    )
  })
})
