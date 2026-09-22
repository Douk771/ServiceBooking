import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SubscriptionModal, type OwnerSubscription } from './AdminPage'
import type { PlanConfig } from '../api/plans'
import type { SubscriptionDiagnostics } from '../api/admin'

// US-63 (API_CONTRACT_CYCLE6.md §42.1/§42.2/§42.4): the "Оплачено до" field must be required
// whenever a plan is assigned, hidden for Free, and the diagnostics panel must surface the
// server's own status text — this is the frontend half that review B3 flagged as missing.

const getSubscriptionHistory = vi.fn()
const getSubscriptionDiagnostics = vi.fn()
const updateSubscription = vi.fn()
const listPlans = vi.fn()

vi.mock('../api/admin', async () => {
  const actual = await vi.importActual<typeof import('../api/admin')>('../api/admin')
  return {
    ...actual,
    adminApi: {
      getSubscriptionHistory: (...args: unknown[]) => getSubscriptionHistory(...args),
      getSubscriptionDiagnostics: (...args: unknown[]) => getSubscriptionDiagnostics(...args),
      updateSubscription: (...args: unknown[]) => updateSubscription(...args),
    },
  }
})

vi.mock('../api/plans', () => ({
  plansApi: {
    list: (...args: unknown[]) => listPlans(...args),
  },
}))

function makePlan(overrides: Partial<PlanConfig> = {}): PlanConfig {
  return {
    id: 'plan-1',
    name: 'Стандарт',
    pricePerMonth: 990,
    maxEmployees: 5,
    maxCompanies: 2,
    allowOnlineBooking: true,
    allowMailing: true,
    allowAnalytics: true,
    allowPublicListing: true,
    allowOnlinePayment: false,
    description: null,
    isActive: true,
    notifyDaysBefore: 1,
    photoQuotaMb: 500,
    photoRetention: 'TwelveMonths',
    ...overrides,
  }
}

function makeDiagnostics(overrides: Partial<SubscriptionDiagnostics> = {}): SubscriptionDiagnostics {
  return {
    ownerUserId: 'owner-1',
    ownerName: 'Иван Иванов',
    planConfigId: 'plan-1',
    planName: 'Стандарт',
    paidUntil: '2026-10-03T23:59:59.999Z',
    isActive: true,
    planIsActive: true,
    status: 'Expired',
    statusText: 'Истёк 3 октября 2026',
    effective: {
      allowOnlineBooking: true,
      allowMailing: true,
      allowAnalytics: true,
      allowPublicListing: true,
      allowOnlinePayment: false,
      maxEmployees: 5,
      maxCompanies: 2,
    },
    companies: [
      {
        companyId: 'co-1',
        name: 'Салон на Ленина',
        allowSelfBooking: true,
        onlineBookingEnabled: false,
        blockingReason: 'SubscriptionExpired',
      },
    ],
    ...overrides,
  }
}

function makeOwner(overrides: Partial<OwnerSubscription> = {}): OwnerSubscription {
  return {
    ownerUserId: 'owner-1',
    ownerEmail: 'owner@example.com',
    planConfigId: 'plan-1',
    paidUntil: '2026-10-03T23:59:59.999Z',
    subscriptionActive: true,
    ...overrides,
  }
}

function renderModal(owner: OwnerSubscription, onClose = vi.fn()) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <SubscriptionModal owner={owner} onClose={onClose} />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getSubscriptionHistory.mockReset().mockResolvedValue([])
  getSubscriptionDiagnostics.mockReset().mockResolvedValue(makeDiagnostics())
  updateSubscription.mockReset().mockResolvedValue(undefined)
  listPlans.mockReset().mockResolvedValue([makePlan(), makePlan({ id: 'plan-free-alike', allowOnlineBooking: false, name: 'Мини' })])
})

describe('SubscriptionModal — US-63', () => {
  it('shows the server-provided diagnostics status text', async () => {
    renderModal(makeOwner())
    expect(await screen.findByText('Истёк 3 октября 2026')).toBeInTheDocument()
    expect(screen.getByText('Салон на Ленина')).toBeInTheDocument()
  })

  it('blocks saving when the paidUntil date is empty for a selected plan', async () => {
    renderModal(makeOwner({ paidUntil: undefined }))
    await screen.findByText('Истёк 3 октября 2026')

    const saveButton = screen.getByRole('button', { name: 'Сохранить' })
    expect(saveButton).toBeDisabled()
    expect(screen.getByText('Укажите дату окончания подписки')).toBeInTheDocument()
    expect(updateSubscription).not.toHaveBeenCalled()
  })

  it('hides the paidUntil field entirely when Free is selected', async () => {
    const user = userEvent.setup()
    renderModal(makeOwner())
    await screen.findByText('Истёк 3 октября 2026')

    expect(screen.getByLabelText('Оплачено до')).toBeInTheDocument()

    const select = screen.getByLabelText('Тарифный план')
    await user.selectOptions(select, '')

    expect(screen.queryByLabelText('Оплачено до')).not.toBeInTheDocument()

    // Free can be saved without a date.
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(updateSubscription).toHaveBeenCalledWith('owner-1', null, null, true, undefined))
  })

  it('warns when the selected plan does not allow online booking', async () => {
    const user = userEvent.setup()
    renderModal(makeOwner({ planConfigId: '' }))
    await screen.findByText('Истёк 3 октября 2026')

    const select = screen.getByLabelText('Тарифный план')
    await user.selectOptions(select, 'plan-free-alike')

    expect(
      screen.getByText('В этом тарифе онлайн-запись выключена — клиенты не смогут записаться сами.'),
    ).toBeInTheDocument()
  })
})
