import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactElement } from 'react'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { BillingPage } from './BillingPage'
import type { OwnerSubscriptionDto } from '../api/billing'

const getSubscription = vi.fn()
const submitRequest = vi.fn()
// Cycle 20 (§432.8) — OperatorDetailsSection queries this unconditionally from BillingPage; 404 is
// its own "nothing to show yet" state (same convention as getSubscription/getTrial), same as every
// other test in this file not caring about the trial-specific calls TrialCard/TrialBanner make.
const getOperatorDetails = vi.fn().mockRejectedValue({ isAxiosError: true, response: { status: 404 } })

vi.mock('../api/billing', async () => {
  const actual = await vi.importActual<typeof import('../api/billing')>('../api/billing')
  return {
    ...actual,
    billingApi: {
      getSubscription: (...args: unknown[]) => getSubscription(...args),
      submitRequest: (...args: unknown[]) => submitRequest(...args),
      cancelRequest: vi.fn(),
      getOperatorDetails: (...args: unknown[]) => getOperatorDetails(...args),
      updateOperatorDetails: vi.fn(),
    },
  }
})

// BillingNoticesSummary (US-20-03) queries this unconditionally alongside the subscription itself.
const getNotices = vi.fn()
vi.mock('../api/platformNotices', () => ({
  platformNoticesApi: { getNotices: (...args: unknown[]) => getNotices(...args) },
}))

beforeEach(() => {
  getSubscription.mockReset()
  getOperatorDetails.mockReset().mockRejectedValue({ isAxiosError: true, response: { status: 404 } })
  getNotices.mockReset().mockResolvedValue({ acknowledgeButtonText: 'Я ознакомился', acknowledgeCaption: '', items: [] })
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

// API_CONTRACT_CYCLE17.md §325.1 (US-17-08, C15-7) — irreversibility notice on a pending request for
// a withdrawn-from-sale plan. Server composes the text; front only prints it.
describe('BillingPage — pendingRequest.irreversibilityNotice (§325.1)', () => {
  it('shows the server-composed notice when present on the pending request', async () => {
    getSubscription.mockResolvedValueOnce(
      makeSubscription({
        pendingRequest: {
          estimatedMonthlyPrice: 1500,
          options: [],
          irreversibilityNotice: 'Вернуться на этот тариф после смены будет невозможно.',
        } as OwnerSubscriptionDto['pendingRequest'],
      }),
    )
    renderWithProviders(<BillingPage />)

    expect(await screen.findByText('Вернуться на этот тариф после смены будет невозможно.')).toBeInTheDocument()
  })

  it('shows nothing extra when irreversibilityNotice is null (public plan)', async () => {
    getSubscription.mockResolvedValueOnce(
      makeSubscription({
        pendingRequest: { estimatedMonthlyPrice: 1500, options: [], irreversibilityNotice: null } as OwnerSubscriptionDto['pendingRequest'],
      }),
    )
    renderWithProviders(<BillingPage />)

    await screen.findByText('Заявка на рассмотрении')
    expect(screen.queryByText(/невозможно/)).not.toBeInTheDocument()
  })
})

// ARCHITECTURE_CYCLE19.md FE-4, API_CONTRACT_CYCLE19.md §408 — a pending request submitted before
// the rollout may still carry опция-лимит lines; the owner must see the server-composed notice and
// a "выведена" mark on those lines, not a silent price mismatch.
describe('BillingPage — pendingRequest.retiredOptionsNotice (cycle 19)', () => {
  it('shows the server-composed notice and marks the retired line, when present', async () => {
    getSubscription.mockResolvedValueOnce(
      makeSubscription({
        pendingRequest: {
          estimatedMonthlyPrice: 1500,
          options: [],
          items: [
            { optionId: 'o1', name: 'Доп. сотрудники', quantity: 3, retired: true },
            { optionId: 'o2', name: 'WhatsApp', quantity: 1, retired: false },
          ],
          retiredOptionsNotice: 'В заявке есть опции, которые больше не подключаются: «Доп. сотрудники».',
        } as OwnerSubscriptionDto['pendingRequest'],
      }),
    )
    renderWithProviders(<BillingPage />)

    expect(
      await screen.findByText('В заявке есть опции, которые больше не подключаются: «Доп. сотрудники».'),
    ).toBeInTheDocument()
    expect(screen.getByText('выведена')).toBeInTheDocument()
  })

  it('shows nothing extra when retiredOptionsNotice is null', async () => {
    getSubscription.mockResolvedValueOnce(
      makeSubscription({
        pendingRequest: {
          estimatedMonthlyPrice: 1500,
          options: [],
          items: [{ optionId: 'o2', name: 'WhatsApp', quantity: 1, retired: false }],
          retiredOptionsNotice: null,
        } as OwnerSubscriptionDto['pendingRequest'],
      }),
    )
    renderWithProviders(<BillingPage />)

    await screen.findByText('Заявка на рассмотрении')
    expect(screen.queryByText('выведена')).not.toBeInTheDocument()
    expect(screen.queryByText(/больше не подключаются/)).not.toBeInTheDocument()
  })
})

// Cycle 24 (ARCHITECTURE_CYCLE24.md §462.2 п.2) — the same screen serves goods with line="Orders".
describe('BillingPage — line (cycle 24)', () => {
  const ordersSub = (over: Record<string, unknown> = {}) =>
    makeSubscription({
      plan: { id: 'p1', name: 'Заказы · Бесплатно', pricePerMonth: 0 },
      usage: { companiesText: '1 магазин из 1', employeesText: '2 участника из 2', numbersText: '0 номеров' },
      ...({
        line: 'Orders',
        orders: { ordersThisMonth: 120, ordersLimit: 150, monthLabel: 'сентябрь', text: 'Заказов в этом месяце: 120 из 150', warningLevel: 'Warning80', allowOrders: true },
        availablePlans: [{ planId: 'pl1', name: 'Заказы · Старт', pricePerMonth: 990, description: 'Для небольшой кухни', highlights: ['до 1000 заказов'], limitsText: '3 магазина, 1000 заказов в месяц' }],
      } as object),
      ...over,
    } as Partial<OwnerSubscriptionDto>)

  it('without the prop asks for the default line and shows none of the Orders blocks (the salon screen is unchanged)', async () => {
    getSubscription.mockResolvedValueOnce(makeSubscription())
    renderWithProviders(<BillingPage />)
    await screen.findByText('Ваша подписка')
    expect(getSubscription).toHaveBeenCalledWith(undefined)
    expect(screen.queryByTestId('available-plans')).toBeNull()
    expect(screen.queryByTestId('orders-usage')).toBeNull()
    expect(screen.getByText(/Смотрите страницу тарифов/)).toBeInTheDocument()
  })

  it('with line="Orders" asks for that line, prints the monthly counter with the 80 % warning and hides salon-only blocks', async () => {
    getSubscription.mockResolvedValueOnce(ordersSub())
    renderWithProviders(<BillingPage line="Orders" />)
    expect(await screen.findByTestId('orders-limit-banner')).toHaveTextContent('Заказов в этом месяце: 120 из 150')
    expect(getSubscription).toHaveBeenCalledWith('Orders')
    expect(screen.getByTestId('orders-usage')).toBeInTheDocument()
    expect(screen.queryByText(/Смотрите страницу тарифов/)).toBeNull()
  })

  it('lists the available plans (L14) and sends a request with the line and the plan', async () => {
    getSubscription.mockResolvedValue(ordersSub())
    submitRequest.mockResolvedValue({ line: 'Orders' })
    const user = userEvent.setup()
    renderWithProviders(<BillingPage line="Orders" />)
    const plans = await screen.findByTestId('available-plans')
    expect(within(plans).getByText('3 магазина, 1000 заказов в месяц')).toBeInTheDocument()
    await user.click(within(plans).getByRole('button', { name: 'Запросить тариф «Заказы · Старт»' }))
    expect(submitRequest).toHaveBeenCalledWith({ line: 'Orders', planId: 'pl1', options: [] })
  })

  it('shows the server text for a request from the other line (409)', async () => {
    getSubscription.mockResolvedValue(ordersSub())
    submitRequest.mockRejectedValue({ isAxiosError: true, response: { status: 409, data: 'У вас уже есть заявка на смену тарифа «Записи» — отмените её или дождитесь решения' } })
    const user = userEvent.setup()
    renderWithProviders(<BillingPage line="Orders" />)
    await user.click(await screen.findByRole('button', { name: 'Запросить тариф «Заказы · Старт»' }))
    expect(await screen.findByText(/У вас уже есть заявка на смену тарифа «Записи»/)).toBeInTheDocument()
  })
})

// Cycle 37 (ARCHITECTURE_CYCLE37.md §37.10.4, API_CONTRACT_CYCLE37.md §37.21.4) — dom mounts the same screen with line="Stays".
describe('BillingPage — line Stays (cycle 37)', () => {
  const staysSub = (stays: Record<string, unknown> | null, over: Record<string, unknown> = {}) =>
    makeSubscription({
      plan: { id: 'p1', name: 'Дома · Старт', pricePerMonth: 1990 },
      usage: { companiesText: '1 компания', employeesText: '3 сотрудника', numbersText: '0 номеров' },
      ...({
        line: 'Stays',
        stays,
        availablePlans: [{ planId: 'ps1', name: 'Дома · Старт', pricePerMonth: 1990, description: null, highlights: null, limitsText: 'до 5 домов' }],
      } as object),
      ...over,
    } as Partial<OwnerSubscriptionDto>)

  it('asks for the Stays line, prints the houses counter and the plan warning, hides salon-only blocks', async () => {
    getSubscription.mockResolvedValueOnce(
      staysSub({ housesPublished: 6, maxHouses: 5, isTrial: false, warningLevel: 'OverLimit', text: 'Опубликовано 6 домов при лимите 5: гости не могут бронировать. Снимите лишние дома с публикации или смените тариф' }),
    )
    renderWithProviders(<BillingPage line="Stays" />)
    expect(await screen.findByTestId('stays-plan-banner')).toHaveTextContent('Опубликовано 6 домов при лимите 5')
    expect(getSubscription).toHaveBeenCalledWith('Stays')
    expect(screen.getByTestId('stays-usage')).toHaveTextContent('Опубликовано домов: 6 из 5')
    expect(screen.queryByText(/Смотрите страницу тарифов/)).toBeNull()
    expect(screen.getByText(/ограничивает число опубликованных домов/)).toBeInTheDocument()
  })

  it('a trial is named, and a warning-free state shows no banner', async () => {
    getSubscription.mockResolvedValueOnce(staysSub({ housesPublished: 1, maxHouses: 3, isTrial: true, trialEndsAtUtc: '2027-02-01T00:00:00Z', warningLevel: 'None', text: null }))
    renderWithProviders(<BillingPage line="Stays" />)
    expect(await screen.findByTestId('stays-usage')).toHaveTextContent('пробный период')
    expect(screen.queryByTestId('stays-plan-banner')).toBeNull()
  })

  it('requests a plan of the Stays line', async () => {
    getSubscription.mockResolvedValue(staysSub({ housesPublished: 0, maxHouses: null, isTrial: false, warningLevel: 'NoPlan', text: 'Тариф не выбран: гости не могут бронировать' }))
    submitRequest.mockResolvedValue({ line: 'Stays' })
    const user = userEvent.setup()
    renderWithProviders(<BillingPage line="Stays" />)
    const plans = await screen.findByTestId('available-plans')
    expect(screen.getByText('Тарифы «Домов»')).toBeInTheDocument()
    await user.click(within(plans).getByRole('button', { name: 'Запросить тариф «Дома · Старт»' }))
    expect(submitRequest).toHaveBeenCalledWith({ line: 'Stays', planId: 'ps1', options: [] })
  })
})
