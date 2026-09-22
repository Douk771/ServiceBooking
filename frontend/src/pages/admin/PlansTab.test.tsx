import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PlansTab } from './PlansTab'
import type { PlanConfig } from '../../api/plans'

// ARCHITECTURE_CYCLE9.md §100.2/§103.1 — the white screen on the stand: `PlansTab.tsx` indexed
// `plan.highlights.length`/`.map` without the `?? []` guard its sibling fields already had, so a
// server on the pre-cycle-7 API shape (no highlights/options/isSystemFree/photoRetention at all)
// threw a TypeError during render and, with no ErrorBoundary anywhere in the tree, took the whole
// app down instead of just this tab. These four cases are the acceptance table from §103.1; the
// first one is expected to fail on the code before that guard was added (US-111).

const listPlans = vi.fn()
const listOptions = vi.fn()

vi.mock('../../api/plans', () => ({
  plansApi: {
    list: (...args: unknown[]) => listPlans(...args),
    listOptions: (...args: unknown[]) => listOptions(...args),
    create: vi.fn(),
    update: vi.fn(),
    deactivate: vi.fn(),
    setSystemFree: vi.fn(),
  },
}))

// A plan literally as a pre-cycle-7 API would have sent it — the four fields the migration added
// (highlights, options, isSystemFree, photoRetention) are entirely absent, not `null`/`[]`.
function preCycle7Plan(overrides: Record<string, unknown> = {}): PlanConfig {
  return {
    id: 'plan-1',
    name: 'Basic',
    description: null,
    pricePerMonth: 990,
    currency: 'RUB',
    maxEmployees: null,
    maxCompanies: null,
    allowOnlineBooking: true,
    allowMailing: false,
    allowAnalytics: false,
    allowPublicListing: true,
    allowOnlinePayment: false,
    photoQuotaMb: null,
    notifyDaysBefore: 7,
    isPublic: true,
    isActive: true,
    sortOrder: 1,
    ...overrides,
  } as unknown as PlanConfig
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <PlansTab />
    </QueryClientProvider>,
  )
}

describe('PlansTab — surviving a pre-cycle-7 API response (US-110/US-111)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    listOptions.mockResolvedValue([])
  })

  it('renders a plan missing `highlights` entirely without throwing, name still shown', async () => {
    listPlans.mockResolvedValue([preCycle7Plan()])

    renderTab()

    await waitFor(() => expect(screen.getByText('Basic')).toBeInTheDocument())
  })

  it('shows the empty state, not a crash, when there are no plans at all', async () => {
    listPlans.mockResolvedValue([])

    renderTab()

    await waitFor(() => expect(screen.getByText('Тарифов ещё нет')).toBeInTheDocument())
  })

  it('still renders the plan list when the option catalog fails to load, and explains instead of showing the matrix', async () => {
    listPlans.mockResolvedValue([preCycle7Plan({ highlights: [], options: [], isSystemFree: false, photoRetention: 'TwelveMonths' })])
    listOptions.mockRejectedValue(new Error('network error'))

    renderTab()

    await waitFor(() => expect(screen.getByText('Basic')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: /Редактировать/ }))
    await waitFor(() => expect(screen.getByText(/Не удалось загрузить каталог опций/)).toBeInTheDocument())
  })

  it('renders a plan missing `options`/`isSystemFree`, edit button still present', async () => {
    listPlans.mockResolvedValue([preCycle7Plan({ highlights: [] })])

    renderTab()

    await waitFor(() => expect(screen.getByText('Basic')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: /Редактировать/ })).toBeInTheDocument()
  })
})
