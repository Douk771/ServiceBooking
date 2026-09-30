import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BillingAccountsAdminTab } from './BillingAccountsAdminTab'
import type { AdminBillingAccountListItem } from '../../api/adminBilling'

// Cycle 28 (API_CONTRACT_CYCLE28.md §594): «Витрина» badge and the showcase filter on the billing-accounts list.

const listAccounts = vi.fn()
vi.mock('../../api/adminBilling', () => ({
  adminBillingApi: { listAccounts: (...a: unknown[]) => listAccounts(...a) },
}))
vi.mock('../../api/plans', () => ({ plansApi: { list: vi.fn(), listOptions: vi.fn() } }))

function item(over: Partial<AdminBillingAccountListItem>): AdminBillingAccountListItem {
  return {
    id: 'acc-1',
    name: null,
    ownerUserId: 'o1',
    ownerName: 'Иван Петров',
    ownerPhoneMasked: null,
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
    ...over,
  } as AdminBillingAccountListItem
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <BillingAccountsAdminTab />
    </QueryClientProvider>,
  )
}

const lastParams = () => listAccounts.mock.calls[listAccounts.mock.calls.length - 1][0]

beforeEach(() => {
  listAccounts.mockReset().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 })
})

describe('BillingAccountsAdminTab — showcase', () => {
  it('shows «Витрина» only on showcase accounts', async () => {
    listAccounts.mockResolvedValue({
      items: [
        item({ id: 'a1', ownerName: 'Витринный Владелец', isShowcase: true }),
        item({ id: 'a2', ownerName: 'Настоящий Владелец', isShowcase: false }),
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    })
    renderTab()

    const showcaseRow = (await screen.findByText('Витринный Владелец')).closest('div.cursor-pointer') as HTMLElement
    const realRow = screen.getByText('Настоящий Владелец').closest('div.cursor-pointer') as HTMLElement
    expect(showcaseRow).toHaveTextContent('Витрина')
    expect(realRow).not.toHaveTextContent('Витрина')
  })

  it('sends no ?showcase= for «Все», and «Только витрина» / «Без витрины» are sent as only / exclude from page 1', async () => {
    const user = userEvent.setup()
    renderTab()
    await waitFor(() => expect(listAccounts).toHaveBeenCalled())
    expect(lastParams().showcase).toBeUndefined()

    await user.click(screen.getByRole('button', { name: 'Только витрина' }))
    await waitFor(() => expect(lastParams().showcase).toBe('only'))
    expect(lastParams().page).toBe(1)

    await user.click(screen.getByRole('button', { name: 'Без витрины' }))
    await waitFor(() => expect(lastParams().showcase).toBe('exclude'))

    await user.click(screen.getByRole('button', { name: 'Все' }))
    await waitFor(() => expect(lastParams().showcase).toBeUndefined())
  })

  it('a failed load shows an error instead of an endless skeleton or a false «не найдено»', async () => {
    listAccounts.mockRejectedValue(new Error('500'))
    renderTab()

    expect(await screen.findByText(/Не удалось загрузить биллинг-аккаунты/)).toBeInTheDocument()
    expect(screen.queryByText('Биллинг-аккаунтов не найдено')).not.toBeInTheDocument()
  })
})
