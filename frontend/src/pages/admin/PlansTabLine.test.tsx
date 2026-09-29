import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PlansTab } from './PlansTab'
import type { PlanConfig } from '../../api/plans'

const listPlans = vi.fn()
const createPlan = vi.fn()
vi.mock('../../api/plans', () => ({
  plansApi: { list: (...a: unknown[]) => listPlans(...a), listOptions: () => Promise.resolve([]), create: (...a: unknown[]) => createPlan(...a), update: vi.fn(), deactivate: vi.fn(), setSystemFree: vi.fn(), setSystemTrial: vi.fn() },
}))

const plan = (over: Record<string, unknown>): PlanConfig =>
  ({ id: 'x', name: 'X', pricePerMonth: 0, currency: 'RUB', maxEmployees: null, maxCompanies: null, allowOnlineBooking: true, allowMailing: false, allowAnalytics: false, allowPublicListing: true, allowOnlinePayment: false, photoQuotaMb: null, notifyDaysBefore: 7, isPublic: true, isActive: true, sortOrder: 1, highlights: [], options: [], isSystemFree: false, ...over }) as unknown as PlanConfig

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}><PlansTab /></QueryClientProvider>)
}

beforeEach(() => {
  listPlans.mockReset().mockResolvedValue([
    plan({ id: 'a', name: 'Салон Про', line: 'Services' }),
    plan({ id: 'legacy', name: 'Старый тариф' }), // an older server: no `line` at all = Записи
    plan({ id: 'o', name: 'Заказы · Бесплатно', line: 'Orders', isSystemFree: true, maxCompanies: 1, maxEmployees: 2, maxProductsPerShop: 50, maxOrdersPerMonth: 150, allowOrders: true }),
  ])
  createPlan.mockReset().mockResolvedValue({})
})

describe('PlansTab — line (cycle 24)', () => {
  it('opens on the salon line, treating a plan without `line` as a salon plan', async () => {
    renderTab()
    expect(await screen.findByText('Салон Про')).toBeInTheDocument()
    expect(screen.getByText('Старый тариф')).toBeInTheDocument()
    expect(screen.queryByText('Заказы · Бесплатно')).toBeNull()
    expect(screen.getByRole('tab', { name: 'Записи' })).toHaveAttribute('aria-selected', 'true')
  })

  it('switches to the «Заказы» line and shows its limits and system-free mark, without salon-only badges or the trial button', async () => {
    const user = userEvent.setup()
    renderTab()
    await user.click(await screen.findByRole('tab', { name: 'Заказы' }))
    expect(await screen.findByText('Заказы · Бесплатно')).toBeInTheDocument()
    expect(screen.queryByText('Салон Про')).toBeNull()
    expect(screen.getByTestId('orders-plan-limits')).toHaveTextContent('Товаров в магазине: до 50 · Заказов в месяц: до 150 · Приём заказов: да')
    expect(screen.getByText('Системный бесплатный')).toBeInTheDocument()
    expect(screen.queryByText('Онлайн-запись')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Сделать тарифом пробного периода' })).toBeNull()
  })

  it('creates a «Заказы» plan with the line and the three new fields; a salon plan is created without them', async () => {
    const user = userEvent.setup()
    renderTab()
    await user.click(await screen.findByRole('tab', { name: 'Заказы' }))
    await user.click(screen.getByRole('button', { name: /Создать тариф/ }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText('Линейка')).toHaveValue('Orders')
    expect(within(dialog).queryByText('Квота фото клиентов, МБ (∞)')).toBeNull()
    await user.type(within(dialog).getByPlaceholderText('Basic'), 'Заказы · Старт')
    await user.type(within(dialog).getByLabelText('Макс. товаров в магазине (∞)'), '200')
    await user.click(within(dialog).getByRole('button', { name: 'Создать' }))
    await waitFor(() => expect(createPlan).toHaveBeenCalled())
    expect(createPlan.mock.calls[0][0]).toMatchObject({ name: 'Заказы · Старт', line: 'Orders', maxProductsPerShop: 200, maxOrdersPerMonth: null, allowOrders: true })
  })

  it('a salon plan body carries no «Заказы» keys', async () => {
    const user = userEvent.setup()
    renderTab()
    await user.click(await screen.findByRole('button', { name: /Создать тариф/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByPlaceholderText('Basic'), 'Салон Новый')
    await user.click(within(dialog).getByRole('button', { name: 'Создать' }))
    await waitFor(() => expect(createPlan).toHaveBeenCalled())
    const body = createPlan.mock.calls[0][0]
    expect('line' in body || 'maxProductsPerShop' in body || 'maxOrdersPerMonth' in body || 'allowOrders' in body).toBe(false)
  })
})
