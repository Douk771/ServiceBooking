import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { BillingPage } from './BillingPage'
import type { OwnerSubscriptionDto } from '../api/billing'

const getSubscription = vi.fn()

vi.mock('../api/billing', async () => {
  const actual = await vi.importActual<typeof import('../api/billing')>('../api/billing')
  return {
    ...actual,
    billingApi: {
      getSubscription: (...args: unknown[]) => getSubscription(...args),
      submitRequest: vi.fn(),
      cancelRequest: vi.fn(),
    },
  }
})

beforeEach(() => {
  getSubscription.mockReset()
})

function renderWithProviders(ui: ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  )
}

function makeSubscription(overrides: Partial<OwnerSubscriptionDto> = {}): OwnerSubscriptionDto {
  return {
    plan: { id: 'p1', name: 'Basic', pricePerMonth: 990 },
    status: 'Active',
    statusText: 'Активна',
    totalMonthlyPrice: 990,
    options: [],
    availableOptions: [],
    usage: { companiesText: '1 компания', employeesText: '2 сотрудника', numbersText: '1 номер' },
    warning: null,
    pendingRequest: null,
    canRequestChanges: true,
    lastRejectedRequest: null,
    ...overrides,
  } as OwnerSubscriptionDto
}

// BLK-4: the server composes a human-readable rejection reason (lastRejectedRequest.reason) when
// an admin turns down the owner's request — the owner must see it verbatim, not guess why.
describe('BillingPage — lastRejectedRequest', () => {
  it('shows the server-composed rejection reason when present', async () => {
    getSubscription.mockResolvedValueOnce(
      makeSubscription({
        lastRejectedRequest: { reason: 'Недостаточно оплаты за выбранные опции.', rejectedAtUtc: '2026-01-01T00:00:00Z' },
      }),
    )
    renderWithProviders(<BillingPage />)

    expect(await screen.findByText('Заявка отклонена')).toBeInTheDocument()
    expect(screen.getByText('Недостаточно оплаты за выбранные опции.')).toBeInTheDocument()
  })

  it('does not show a rejection card when lastRejectedRequest is null', async () => {
    getSubscription.mockResolvedValueOnce(makeSubscription())
    renderWithProviders(<BillingPage />)

    await screen.findByText('Ваша подписка')
    expect(screen.queryByText('Заявка отклонена')).not.toBeInTheDocument()
  })

  it('hides the rejection card once a new request is pending', async () => {
    getSubscription.mockResolvedValueOnce(
      makeSubscription({
        lastRejectedRequest: { reason: 'Причина отказа.', rejectedAtUtc: '2026-01-01T00:00:00Z' },
        pendingRequest: { estimatedMonthlyPrice: 1500, options: [] } as OwnerSubscriptionDto['pendingRequest'],
      }),
    )
    renderWithProviders(<BillingPage />)

    await screen.findByText('Заявка на рассмотрении')
    expect(screen.queryByText('Заявка отклонена')).not.toBeInTheDocument()
  })
})
