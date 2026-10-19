import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PlansTab } from './PlansTab'
import type { AdminOptionDto, PlanConfig } from '../../api/plans'

// ARCHITECTURE_CYCLE9.md §100.2/§103.1 — the white screen on the stand: `PlansTab.tsx` indexed
// `plan.highlights.length`/`.map` without the `?? []` guard its sibling fields already had, so a
// server on the pre-cycle-7 API shape (no highlights/options/isSystemFree/photoRetention at all)
// threw a TypeError during render and, with no ErrorBoundary anywhere in the tree, took the whole
// app down instead of just this tab. These four cases are the acceptance table from §103.1; the
// first one is expected to fail on the code before that guard was added (US-111).

const listPlans = vi.fn()
const listOptions = vi.fn()
const updatePlan = vi.fn()

vi.mock('../../api/plans', () => ({
  plansApi: {
    list: (...args: unknown[]) => listPlans(...args),
    listOptions: (...args: unknown[]) => listOptions(...args),
    create: vi.fn(),
    update: (...args: unknown[]) => updatePlan(...args),
    deactivate: vi.fn(),
    setSystemFree: vi.fn(),
  },
}))

// Cycle 43 (§43.5.3): the matrix drops WhatsApp / MAX and points to the notifications tab; the ids come from the messenger-options answer.
const messengerOptions = vi.fn()
vi.mock('../../api/adminBilling', () => ({
  adminBillingApi: { messengerOptions: (...args: unknown[]) => messengerOptions(...args) },
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

// ARCHITECTURE_CYCLE19.md FE-5 — the plan form's Max employees/Max companies fields are the only
// place the limit is set since cycle 19; the hint must say so directly under both fields.
describe('PlansTab — retired-limit-options hint (cycle 19)', () => {
  beforeEach(() => {
    listPlans.mockResolvedValue([])
    listOptions.mockResolvedValue([])
  })

  it('shows the hint under both limit fields when creating a plan', async () => {
    renderTab()
    await userEvent.click(await screen.findByText('Создать тариф'))

    const hints = screen.getAllByText('Лимит задаётся только здесь; опций для докупки сотрудников и компаний нет.')
    expect(hints).toHaveLength(2)
  })
})

// ARCHITECTURE_CYCLE19.md §386.2 / SPEC §391.1 (risk Р19-3) — the options matrix must render
// exactly what the catalog endpoint returns and must not apply any client-side filter of its own
// (e.g. hiding extra-employees/extra-companies rows), because a frontend build shipped ahead of a
// backend that has not yet retired those rows would otherwise silently drop the ability to edit
// them. This asserts the round trip: a catalog option shows up as a row, and toggling it to
// "Включена" with a quantity is present verbatim in the PUT body.
describe('PlansTab — options matrix renders exactly the catalog, no client-side filter (§391.1)', () => {
  const employeesOption: AdminOptionDto = {
    id: 'option-employees',
    code: 'extra-employees',
    name: 'Дополнительные сотрудники',
    kind: 'Quantity',
    capabilityKey: 'employees',
    capabilityKnown: true,
    pricePerMonth: 100,
    currency: 'RUB',
    unitName: 'сотрудник',
    maxQuantity: null,
    isPublic: true,
    isActive: true,
  } as unknown as AdminOptionDto

  beforeEach(() => {
    vi.clearAllMocks()
    listPlans.mockResolvedValue([preCycle7Plan({ highlights: [], options: [], isSystemFree: false })])
    listOptions.mockResolvedValue([employeesOption])
    updatePlan.mockResolvedValue(preCycle7Plan({}))
  })

  it('renders a row for every catalog option, with no filtering by capabilityKey', async () => {
    renderTab()
    await waitFor(() => expect(screen.getByText('Basic')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: /Редактировать/ }))

    expect(await screen.findByText('Дополнительные сотрудники')).toBeInTheDocument()
  })

  it('sends the option availability set in the matrix verbatim in the PUT body', async () => {
    renderTab()
    await waitFor(() => expect(screen.getByText('Basic')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: /Редактировать/ }))
    await screen.findByText('Дополнительные сотрудники')

    const select = screen
      .getAllByRole('combobox')
      .find((el) => within(el as HTMLSelectElement).queryByText('Включена'))!
    await userEvent.selectOptions(select, 'Включена')
    const quantityInput = await screen.findByPlaceholderText('кол-во')
    await userEvent.type(quantityInput, '3')

    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(updatePlan).toHaveBeenCalled())
    const [, payload] = updatePlan.mock.calls[0]
    expect(payload.options).toEqual([
      { optionId: 'option-employees', availability: 'Included', includedQuantity: 3 },
    ])
  })
})

const MATRIX_NOTE = 'Мессенджеры WhatsApp и MAX подключаются отдельно от тарифа. Цена — во вкладке уведомлений'
const messengerCatalog = {
  items: [{ optionId: 'opt-wa' }, { optionId: 'opt-max' }],
  planMatrixNote: MATRIX_NOTE,
  priceHint: 'h',
}

describe('PlansTab — messengers are not a plan feature (cycle 43, US-43-02)', () => {
  const opt = (id: string, name: string, code: string) =>
    ({ id, code, name, kind: 'Toggle', capabilityKnown: true, pricePerMonth: 1, currency: 'RUB', isPublic: true, isActive: true }) as unknown as AdminOptionDto

  beforeEach(() => {
    vi.clearAllMocks()
    listPlans.mockResolvedValue([preCycle7Plan({ highlights: [], options: [], isSystemFree: false })])
    messengerOptions.mockResolvedValue(messengerCatalog)
  })

  it('leaves the other options in the matrix, drops WhatsApp / MAX and prints the server note under it', async () => {
    listOptions.mockResolvedValue([opt('opt-wa', 'WhatsApp', 'notifications.whatsapp'), opt('opt-x', 'Дополнительный отчёт', 'extra.report'), opt('opt-max', 'MAX', 'notifications.max')])
    renderTab()
    await userEvent.click(await screen.findByRole('button', { name: /Редактировать/ }))
    expect(await screen.findByText('Дополнительный отчёт')).toBeInTheDocument()
    expect(screen.queryByText('WhatsApp')).not.toBeInTheDocument()
    expect(screen.queryByText('MAX')).not.toBeInTheDocument()
    expect(screen.getByText(MATRIX_NOTE)).toBeInTheDocument()
  })

  it('replaces the whole matrix with the note when only messengers are left, and still drops them if the messenger answer failed', async () => {
    listOptions.mockResolvedValue([opt('opt-wa', 'WhatsApp', 'notifications.whatsapp'), opt('opt-max', 'MAX', 'notifications.max')])
    messengerOptions.mockRejectedValue(new Error('down'))
    renderTab()
    await userEvent.click(await screen.findByRole('button', { name: /Редактировать/ }))
    expect(await screen.findByText(MATRIX_NOTE)).toBeInTheDocument()
    expect(screen.queryByText('WhatsApp')).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: /WhatsApp/ })).not.toBeInTheDocument()
  })
})

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

// ARCHITECTURE_CYCLE15.md §255.1 — formToPayload() used to send isActive/isPublic/sortOrder as
// hardcoded constants on every save, which silently put any plan back on the storefront and reset
// its display order. This test fails on the pre-fix code (it observed `isPublic: true` in the PUT
// body no matter what the checkbox said).
describe('PlansTab — §255.1 formToPayload no longer sends isActive/isPublic/sortOrder as constants', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    listOptions.mockResolvedValue([])
  })

  it('unchecking "Показывать на витрине" and saving sends isPublic: false, preserves sortOrder', async () => {
    listPlans.mockResolvedValue([preCycle7Plan({ highlights: [], options: [], isSystemFree: false, sortOrder: 3 })])
    updatePlan.mockResolvedValue(preCycle7Plan({ isPublic: false, sortOrder: 3 }))

    renderTab()

    await waitFor(() => expect(screen.getByText('Basic')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: /Редактировать/ }))

    const publicCheckbox = await screen.findByLabelText(/Показывать на витрине/)
    expect(publicCheckbox).toBeChecked()
    await userEvent.click(publicCheckbox)

    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(updatePlan).toHaveBeenCalled())
    const [, payload] = updatePlan.mock.calls[0]
    expect(payload.isPublic).toBe(false)
    expect(payload.isActive).toBe(true)
    expect(payload.sortOrder).toBe(3)
  })
})
