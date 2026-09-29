import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BillingAccountsAdminTab } from './BillingAccountsAdminTab'
import type { AdminBillingAccount, AdminBillingAccountListItem, AdminSubscriptionRequest } from '../../api/adminBilling'
import type { PlanConfig, AdminOptionDto } from '../../api/plans'

// QA cycle 7 (final pass before merge) — the "save blocked without paid-until", "date field hidden
// for a free plan" and "warning-not-block for a plan without online booking" behaviours were only
// exercised at the level of the pure helpers (billingAccountsHelpers.test.ts). Nothing rendered the
// actual modal, so a regression that disconnected a helper's return value from the JSX (e.g. someone
// stops passing `disabled={dateMissing}` to the Save button) would sail through green. Written from
// SPEC.md/ARCHITECTURE_CYCLE6.md §43.3.6, independently of the component's own implementation.

const listAccounts = vi.fn()
const getAccount = vi.fn()
const assignSubscription = vi.fn()
const getHistory = vi.fn()
const listRequests = vi.fn()

vi.mock('../../api/adminBilling', () => ({
  adminBillingApi: {
    listAccounts: (...args: unknown[]) => listAccounts(...args),
    getAccount: (...args: unknown[]) => getAccount(...args),
    assignSubscription: (...args: unknown[]) => assignSubscription(...args),
    getHistory: (...args: unknown[]) => getHistory(...args),
    listRequests: (...args: unknown[]) => listRequests(...args),
    rejectRequest: vi.fn(),
  },
}))

const listPlans = vi.fn()
const listOptions = vi.fn()

vi.mock('../../api/plans', () => ({
  plansApi: {
    list: (...args: unknown[]) => listPlans(...args),
    listOptions: (...args: unknown[]) => listOptions(...args),
  },
}))

function paidPlan(overrides: Partial<PlanConfig> = {}): PlanConfig {
  return {
    id: 'plan-paid',
    name: 'PRO',
    description: null,
    highlights: [],
    pricePerMonth: 1500,
    currency: 'RUB',
    maxEmployees: null,
    maxCompanies: null,
    allowOnlineBooking: true,
    allowMailing: true,
    allowAnalytics: true,
    allowPublicListing: true,
    allowOnlinePayment: true,
    photoQuotaMb: 1000,
    photoRetention: 'TwelveMonths',
    notifyDaysBefore: 7,
    isPublic: true,
    isActive: true,
    isSystemFree: false,
    sortOrder: 1,
    options: [],
    subscribedAccounts: 0,
    ...overrides,
  } as PlanConfig
}

function listItem(overrides: Partial<AdminBillingAccountListItem> = {}): AdminBillingAccountListItem {
  return {
    id: 'acc-1',
    name: null,
    ownerUserId: 'owner-1',
    ownerName: 'Иван Петров',
    ownerPhoneMasked: '+7 *** *** 12 34',
    planName: null,
    status: 'Free',
    statusText: 'Бесплатный тариф',
    paidUntil: null,
    totalMonthlyPrice: 0,
    currency: 'RUB',
    companiesUsed: 1,
    companiesLimit: 1,
    employeesUsed: 1,
    employeesLimit: 1,
    numbersPaid: 0,
    numbersRegistered: 0,
    hasPendingRequest: false,
    ...overrides,
  } as AdminBillingAccountListItem
}

function account(overrides: Partial<AdminBillingAccount> = {}): AdminBillingAccount {
  return {
    id: 'acc-1',
    name: null,
    ownerUserId: 'owner-1',
    ownerName: 'Иван Петров',
    ownerPhoneMasked: '+7 *** *** 12 34',
    currency: 'RUB',
    status: 'Free',
    statusText: 'Бесплатный тариф',
    isActive: true,
    planId: null,
    plan: { id: null, name: 'Бесплатный', description: null, pricePerMonth: 0, includes: [] },
    options: [],
    totalMonthlyPrice: 0,
    paidUntil: null,
    companiesUsed: 1,
    companiesLimit: 1,
    employeesUsed: 1,
    employeesLimit: 1,
    numbersPaid: 0,
    numbersRegistered: 0,
    grandfatheredEmployeeBonus: 0,
    grandfatheredEmployeeBonusText: null,
    companies: [],
    channels: [],
    pendingRequest: null,
    ...overrides,
  } as AdminBillingAccount
}

function subscriptionRequest(overrides: Partial<AdminSubscriptionRequest> = {}): AdminSubscriptionRequest {
  return {
    id: 'req-1',
    billingAccountId: 'acc-1',
    accountName: null,
    requestedByName: 'Иван Петров',
    requestedByPhoneMasked: null,
    createdAt: '2026-09-01T10:00:00Z',
    status: 'Pending',
    currentPlanName: null,
    desiredPlanName: 'PRO',
    items: [],
    estimatedMonthlyPrice: 1500,
    comment: null,
    companiesCount: 1,
    retiredOptionsNotice: null,
    ...overrides,
  } as AdminSubscriptionRequest
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <BillingAccountsAdminTab />
    </QueryClientProvider>,
  )
}

async function openAssignModal() {
  await waitFor(() => expect(screen.getByText('Иван Петров')).toBeInTheDocument())
  await userEvent.click(screen.getByText('Иван Петров'))
  await waitFor(() => expect(screen.getByText('Назначить подписку')).toBeInTheDocument())
  await userEvent.click(screen.getByText('Назначить подписку'))
  await waitFor(() => expect(screen.getByText('Тарифный план')).toBeInTheDocument())
  const heading = screen.getByText(/^Назначить подписку —/)
  const container = heading.closest('.bg-cream') as HTMLElement
  return { ...within(container), container }
}

describe('BillingAccountsAdminTab — assign subscription modal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    listAccounts.mockResolvedValue({ items: [listItem()], page: 1, pageSize: 20, totalCount: 1 })
    getAccount.mockResolvedValue(account())
    getHistory.mockResolvedValue([])
    listPlans.mockResolvedValue([paidPlan(), paidPlan({ id: 'plan-no-booking', name: 'Без записи', allowOnlineBooking: false })])
    listOptions.mockResolvedValue([] as AdminOptionDto[])
  })

  it('hides the "Оплачено до" field entirely while the free plan is selected', async () => {
    await renderTab()
    const modal = await openAssignModal()

    expect(modal.queryByText('Оплачено до')).not.toBeInTheDocument()
    expect(modal.container.querySelector('input[type="date"]')).not.toBeInTheDocument()
    // The Save button asserts nothing is blocking a free-plan save.
    expect(modal.getByRole('button', { name: 'Сохранить' })).toBeEnabled()
  })

  it('shows the field and blocks Save once a paid plan is chosen without a date, and unblocks once a date is set', async () => {
    await renderTab()
    const modal = await openAssignModal()

    await userEvent.selectOptions(modal.getByRole('combobox'), 'plan-paid')

    expect(modal.getByText('Укажите дату окончания подписки')).toBeInTheDocument()
    expect(modal.getByRole('button', { name: 'Сохранить' })).toBeDisabled()

    const dateInput = modal.container.querySelector('input[type="date"]') as HTMLInputElement
    expect(dateInput).toBeTruthy()
    await userEvent.type(dateInput, '2026-12-31')

    await waitFor(() => expect(modal.queryByText('Укажите дату окончания подписки')).not.toBeInTheDocument())
    expect(modal.getByRole('button', { name: 'Сохранить' })).toBeEnabled()
  })

  it('warns, but does NOT block Save, when the selected plan has online booking switched off', async () => {
    await renderTab()
    const modal = await openAssignModal()

    await userEvent.selectOptions(modal.getByRole('combobox'), 'plan-no-booking')
    const dateInput = modal.container.querySelector('input[type="date"]') as HTMLInputElement
    await userEvent.type(dateInput, '2026-12-31')

    expect(modal.getByText(/онлайн-запись выключена/i)).toBeInTheDocument()
    expect(modal.getByRole('button', { name: 'Сохранить' })).toBeEnabled()
  })
})

describe('BillingAccountsAdminTab — server-supplied statusText only', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getHistory.mockResolvedValue([])
    listPlans.mockResolvedValue([])
    listOptions.mockResolvedValue([])
  })

  it('renders the exact statusText the server sent in the accounts list, without a client-side dictionary', async () => {
    const weirdServerText = 'Оплачено до 5 сен 2026 (испытательная фраза сервера)'
    listAccounts.mockResolvedValue({
      items: [listItem({ status: 'Active', statusText: weirdServerText })],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    })

    await renderTab()
    await waitFor(() => expect(screen.getByText(weirdServerText)).toBeInTheDocument())
  })

  it('renders the exact statusText the server sent in the account detail card', async () => {
    listAccounts.mockResolvedValue({ items: [listItem()], page: 1, pageSize: 20, totalCount: 1 })
    const weirdServerText = 'Подписка неактивна (испытательная фраза сервера)'
    getAccount.mockResolvedValue(account({ status: 'Expired', statusText: weirdServerText }))

    await renderTab()
    await waitFor(() => expect(screen.getByText('Иван Петров')).toBeInTheDocument())
    await userEvent.click(screen.getByText('Иван Петров'))

    await waitFor(() => expect(screen.getByText(weirdServerText)).toBeInTheDocument())
  })
})

// ARCHITECTURE_CYCLE19.md FE-3, API_CONTRACT_CYCLE19.md §407 — a request submitted before the
// rollout may carry опция-лимит lines; the superadmin must see the server-composed notice and a
// "выведена" mark BOTH in the queue card and in the approval modal, before confirming.
describe('BillingAccountsAdminTab — retiredOptionsNotice in the requests queue (cycle 19)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    listAccounts.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 })
    getAccount.mockResolvedValue(account())
    getHistory.mockResolvedValue([])
    listPlans.mockResolvedValue([])
    listOptions.mockResolvedValue([])
  })

  it('marks a retired line struck through and shows the notice in the queue card', async () => {
    listRequests.mockResolvedValue({
      items: [
        subscriptionRequest({
          items: [
            { optionId: 'o1', name: 'Доп. сотрудники', quantity: 2, retired: true },
            { optionId: 'o2', name: 'WhatsApp', quantity: 1, retired: false },
          ],
          retiredOptionsNotice: 'В заявке есть опции, которые больше не подключаются: «Доп. сотрудники».',
        }),
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    })

    await renderTab()
    await userEvent.click(screen.getByText('Заявки'))

    expect(
      await screen.findByText('В заявке есть опции, которые больше не подключаются: «Доп. сотрудники».'),
    ).toBeInTheDocument()
    expect(screen.getByText(/выведена/)).toBeInTheDocument()
  })

  it('shows the notice in the approval modal BEFORE the confirm button, built from the option catalog', async () => {
    listRequests.mockResolvedValue({
      items: [
        subscriptionRequest({
          items: [{ optionId: 'o1', name: 'Доп. сотрудники', quantity: 2, retired: true }],
          retiredOptionsNotice: 'В заявке есть опции, которые больше не подключаются: «Доп. сотрудники».',
        }),
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    })

    await renderTab()
    await userEvent.click(screen.getByText('Заявки'))
    await screen.findByText('Иван Петров')
    await userEvent.click(screen.getByText('Одобрить'))

    await waitFor(() => expect(screen.getByText('Тарифный план')).toBeInTheDocument())
    expect(
      screen.getAllByText('В заявке есть опции, которые больше не подключаются: «Доп. сотрудники».'),
    ).toHaveLength(2)
  })

  it('renders nothing extra when retiredOptionsNotice is null', async () => {
    listRequests.mockResolvedValue({
      items: [subscriptionRequest({ items: [{ optionId: 'o2', name: 'WhatsApp', quantity: 1, retired: false }] })],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    })

    await renderTab()
    await userEvent.click(screen.getByText('Заявки'))

    await screen.findByText('Иван Петров')
    expect(screen.queryByText(/больше не подключаются/)).not.toBeInTheDocument()
    expect(screen.queryByText(/выведена/)).not.toBeInTheDocument()
  })
})
