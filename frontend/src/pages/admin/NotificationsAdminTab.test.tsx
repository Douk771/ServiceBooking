import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NotificationsAdminTab } from './NotificationsAdminTab'
import type { PlatformSettings } from '../../types'
import type { AdminChannelDto, AdminChannelSummaryDto } from '../../api/notificationNumbers'

const listChannels = vi.fn()
const summary = vi.fn()
const confirmPayment = vi.fn()
const card = vi.fn()
const getSettings = vi.fn()
const updateSettings = vi.fn()
const suspend = vi.fn()

vi.mock('../../api/platformSettings', () => ({
  adminNotificationsApi: {
    getSettings: (...args: unknown[]) => getSettings(...args),
    suspend: (...args: unknown[]) => suspend(...args),
    resume: vi.fn(),
    updateSettings: (...args: unknown[]) => updateSettings(...args),
  },
}))
vi.mock('../../api/notificationNumbers', () => ({
  adminNumbersApi: {
    list: (...args: unknown[]) => listChannels(...args),
    summary: (...args: unknown[]) => summary(...args),
    confirmPayment: (...args: unknown[]) => confirmPayment(...args),
    card: (...args: unknown[]) => card(...args),
  },
}))

function channelsPage(items: AdminChannelDto[]) {
  return { items, page: 1, pageSize: 20, total: items.length }
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
    paidUntil: null,
    companyCount: 1,
    idleSince: null,
    requestedAt: null,
    inn: null,
    legalEntityForm: null,
    displayStatus: 'Working',
    displayText: 'Сообщения уходят с номера +7 *** ***-45-67',
    paymentText: 'оплачено до 20.11.2026',
    isSuspended: false,
    createdAt: '2026-10-01T09:00:00Z',
    availableActions: ['Suspend', 'ConfirmPayment'],
    ...overrides,
  } as AdminChannelDto
}

function emptySummary(): AdminChannelSummaryDto {
  return {
    connected: 0,
    connecting: 0,
    disconnected: 0,
    blocked: 0,
    needsReconnect: 0,
    idle: 0,
    expiringIn7Days: 0,
    pendingRequests: 0,
    working: 0,
    actionRequired: 0,
    off: 0,
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
    customerMessagingEnabled: true,
    whatsAppOptionOpen: false,
    maxOptionOpen: true,
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
  confirmPayment.mockReset()
  card.mockReset()
  suspend.mockReset()
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

  it('sends null for trial fields left untouched, so an unrelated save never overwrites them (§367: null = leave unchanged)', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([]))
    updateSettings.mockResolvedValue(platformSettings())
    renderTab()

    await screen.findByLabelText('Длительность (дней)')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() =>
      expect(updateSettings).toHaveBeenCalledWith(
        expect.objectContaining({
          trialDurationDays: null,
          trialMailingWindowDays: null,
          trialWarningThresholdsDays: null,
        }),
      ),
    )
  })

  it('sends only the trial field the admin actually edited, leaving the others null', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([]))
    updateSettings.mockResolvedValue(platformSettings())
    renderTab()

    const durationInput = await screen.findByLabelText('Длительность (дней)')
    await user.clear(durationInput)
    await user.type(durationInput, '21')

    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() =>
      expect(updateSettings).toHaveBeenCalledWith(
        expect.objectContaining({
          trialDurationDays: 21,
          trialMailingWindowDays: null,
          trialWarningThresholdsDays: null,
        }),
      ),
    )
  })
})

describe('NotificationsAdminTab — the table, «Подтвердить оплату» and the availability switches (cycle 40)', () => {
  it('shows a transport badge per number and re-queries with the selected ?transport= filter', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([channel({ transport: 'Max' })]))
    renderTab()

    const row = (await screen.findByText('Салон Люкс')).closest('div.p-4') as HTMLElement
    expect(within(row).getByText('MAX')).toBeInTheDocument()
    expect(within(row).getByText('Работает')).toBeInTheDocument()
    expect(within(row).getByText('оплачено до 20.11.2026')).toBeInTheDocument()
    await waitFor(() => expect(listChannels).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20, transport: undefined })))

    listChannels.mockClear()
    listChannels.mockResolvedValue(channelsPage([channel({ transport: 'Max' })]))
    await user.selectOptions(screen.getByLabelText('Канал'), 'Max')
    await waitFor(() => expect(listChannels).toHaveBeenCalledWith(expect.objectContaining({ page: 1, transport: 'Max' })))
  })

  it('filters by the status, by the payment and with the replaced numbers on request', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([]))
    renderTab()
    await screen.findByLabelText('Статус')

    await user.selectOptions(screen.getByLabelText('Статус'), 'ActionRequired')
    await waitFor(() => expect(listChannels).toHaveBeenCalledWith(expect.objectContaining({ displayStatus: 'ActionRequired' })))
    await user.selectOptions(screen.getByLabelText('Оплата'), 'Requested')
    await waitFor(() => expect(listChannels).toHaveBeenCalledWith(expect.objectContaining({ payment: 'Requested' })))
    await user.click(screen.getByLabelText('Показать заменённые'))
    await waitFor(() => expect(listChannels).toHaveBeenCalledWith(expect.objectContaining({ includeReplaced: true })))
  })

  it('shows only the actions the server allows: a replaced number has none', async () => {
    listChannels.mockResolvedValue(channelsPage([channel({ availableActions: [], displayStatus: null, displayText: null })]))
    renderTab()
    const row = (await screen.findByText('Салон Люкс')).closest('div.p-4') as HTMLElement
    expect(within(row).queryByRole('button', { name: 'Подтвердить оплату' })).not.toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: 'Приостановить' })).not.toBeInTheDocument()
  })

  it('confirms a payment for the chosen number of months and refreshes; the server refusal is printed as it is', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([channel({ displayStatus: 'ActionRequired', displayText: 'Оплата на проверке', paymentText: 'заявка от 20.10' })]))
    confirmPayment.mockRejectedValueOnce({ response: { status: 409, data: 'Опция MAX закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“' } })
    confirmPayment.mockResolvedValueOnce({})
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Подтвердить оплату' }))
    const dialog = screen.getByRole('dialog')
    await user.clear(within(dialog).getByLabelText('На сколько месяцев (1–12)'))
    await user.type(within(dialog).getByLabelText('На сколько месяцев (1–12)'), '3')
    await user.type(within(dialog).getByLabelText('Комментарий (счёт, платёж)'), 'Счёт 17')
    await user.click(within(dialog).getByRole('button', { name: 'Подтвердить' }))

    expect(await within(dialog).findByText(/закрыта для подключения/)).toBeInTheDocument()
    expect(confirmPayment).toHaveBeenCalledWith('ch1', { months: 3, comment: 'Счёт 17' })

    await user.click(within(dialog).getByRole('button', { name: 'Подтвердить' }))
    await waitFor(() => expect(confirmPayment).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('switches an option open for owners and the stop-cock of customer messaging without a release', async () => {
    const user = userEvent.setup()
    listChannels.mockResolvedValue(channelsPage([]))
    updateSettings.mockResolvedValue({ ...platformSettings(), whatsAppOptionOpen: true })
    renderTab()

    const toggle = await screen.findByRole('switch', { name: 'WhatsApp — открыто для владельцев' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')
    expect(screen.getByRole('switch', { name: 'MAX — открыто для владельцев' })).toHaveAttribute('aria-checked', 'true')
    await user.click(toggle)
    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith(expect.objectContaining({
      whatsAppOptionOpen: true, trialDurationDays: null, trialMailingWindowDays: null, trialWarningThresholdsDays: null,
    })))
  })

  it('no longer offers a price field for the «channel» option', async () => {
    listChannels.mockResolvedValue(channelsPage([]))
    renderTab()
    await screen.findByText('Параметры номеров')
    expect(screen.queryByLabelText(/Цена опции/)).not.toBeInTheDocument()
  })
})
