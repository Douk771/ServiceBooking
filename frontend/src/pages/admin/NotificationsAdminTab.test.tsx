import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NotificationsAdminTab } from './NotificationsAdminTab'
import type { AdminChannelDto, AdminChannelSummary, PlatformSettings, Paged } from '../../types'

const listChannels = vi.fn()
const summary = vi.fn()
const getSettings = vi.fn()
const updateSettings = vi.fn()

vi.mock('../../api/platformSettings', () => ({
  adminNotificationsApi: {
    listChannels: (...args: unknown[]) => listChannels(...args),
    summary: (...args: unknown[]) => summary(...args),
    getSettings: (...args: unknown[]) => getSettings(...args),
    suspend: vi.fn(),
    resume: vi.fn(),
    updateSettings: (...args: unknown[]) => updateSettings(...args),
  },
}))

function channelsPage(items: AdminChannelDto[]): Paged<AdminChannelDto> {
  return { items, page: 1, pageSize: 20, total: items.length, hasNext: false }
}

function channel(overrides: Partial<AdminChannelDto> = {}): AdminChannelDto {
  return {
    id: 'ch1',
    transport: 'WhatsApp',
    ownerName: 'Салон Люкс',
    ownerPhoneMasked: '+7 *** *** 12 34',
    state: 'Connected',
    stateText: 'подключён',
    paymentState: 'Paid',
    paidFrom: null,
    paidUntil: null,
    companyCount: 1,
    idleSince: null,
    requestedAt: null,
    inn: null,
    legalEntityForm: null,
    ...overrides,
  }
}

function emptySummary(): AdminChannelSummary {
  return {
    connected: 0,
    connecting: 0,
    disconnected: 0,
    blocked: 0,
    needsReconnect: 0,
    idle: 0,
    expiringIn7Days: 0,
    pendingRequests: 0,
  }
}

function platformSettings(): PlatformSettings {
  return {
    channelPricePerMonth: 990,
    channelIdleDays: 3,
    pricingPublicEnabled: false,
    pricingPublicBlockedReason: null,
    trialDurationDays: 14,
    trialMailingWindowDays: 7,
    trialWarningThresholdsDays: [7, 3, 1],
  }
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <NotificationsAdminTab />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  listChannels.mockReset()
  summary.mockReset()
  getSettings.mockReset()
  updateSettings.mockReset()
  summary.mockResolvedValue(emptySummary())
  getSettings.mockResolvedValue(platformSettings())
})

describe('NotificationsAdminTab — trial settings (API_CONTRACT_CYCLE18.md §367)', () => {
  it('blocks saving and never calls the API when the mailing window is set longer than the trial duration', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([]))
    renderTab()

    const durationInput = await screen.findByLabelText('Длительность (дней)')
    const windowInput = screen.getByLabelText('Окно рассылок (дней)')

    await user.clear(windowInput)
    await user.type(windowInput, '20')
    await user.clear(durationInput)
    await user.type(durationInput, '14')

    expect(await screen.findByText('Окно рассылок не может быть длиннее длительности пробного периода.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(updateSettings).not.toHaveBeenCalled()
    expect(screen.getAllByText('Окно рассылок не может быть длиннее длительности пробного периода.').length).toBeGreaterThan(0)
  })

  it('saves trial settings alongside the existing channel settings when valid', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([]))
    updateSettings.mockResolvedValue(platformSettings())
    renderTab()

    await screen.findByLabelText('Длительность (дней)')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() =>
      expect(updateSettings).toHaveBeenCalledWith(
        expect.objectContaining({
          trialDurationDays: 14,
          trialMailingWindowDays: 7,
          trialWarningThresholdsDays: [7, 3, 1],
        }),
      ),
    )
  })
})

describe('NotificationsAdminTab — transport in the channel list (§114.3)', () => {
  it('shows a transport badge per channel and re-queries with the selected ?transport= filter', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([channel({ transport: 'Max' })]))
    renderTab()

    const row = (await screen.findByText('Салон Люкс')).closest('div.p-4') as HTMLElement
    expect(within(row).getByText('MAX')).toBeInTheDocument()
    await waitFor(() =>
      expect(listChannels).toHaveBeenCalledWith({ page: 1, pageSize: 20, transport: undefined }),
    )

    listChannels.mockClear()
    listChannels.mockResolvedValue(channelsPage([channel({ transport: 'Max' })]))
    await user.selectOptions(screen.getByLabelText('Канал'), 'Max')

    await waitFor(() => expect(listChannels).toHaveBeenCalledWith({ page: 1, pageSize: 20, transport: 'Max' }))
  })
})
