import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BookingRulesSection } from './BookingRulesSection'
import { SalonProfileSection } from './SalonProfileSection'
import { CANCEL_WINDOW_FIELD_CAPTION } from '../../legal/staffNotices'
import type { Company } from '../../types'

const update = vi.fn()
// Rejections go through a plain function: a spy re-throws a recorded rejection as an unhandled error.
let failWith: unknown = null
vi.mock('../../api/companies', () => ({ companiesApi: { update: (...a: unknown[]) => (failWith ? Promise.reject(failWith) : update(...a)), uploadLogo: vi.fn() } }))
vi.mock('../../api/companyAddress', () => ({ companyAddressApi: { saveAddress: vi.fn() } }))
vi.mock('../../api/cities', () => ({ citiesApi: { search: () => Promise.resolve([]) } }))

const company = (over: Partial<Company> = {}): Company =>
  ({
    id: 'co1', name: 'Салон', slug: 's', allowSelfBooking: true, requirePrepayment: false, bookingHorizonDays: 90,
    clientRescheduleMinHours: 2, planAllowsOnlineBooking: true, planAllowsOnlinePayment: true, ...over,
  }) as Company

function renderIt(c: Company, withProfile = false) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      {withProfile && <SalonProfileSection key="p" company={c} />}
      <BookingRulesSection key="r" company={c} />
    </QueryClientProvider>,
  )
}
const H = 'На сколько дней вперёд клиент может записаться'
const W = 'За сколько часов клиент может перенести или отменить запись'
const saveRules = (u: ReturnType<typeof userEvent.setup>) => u.click(screen.getByRole('button', { name: 'Сохранить правила' }))
const err = (status: number, data: string) => ({ isAxiosError: true, response: { status, data } })

beforeEach(() => {
  failWith = null
  update.mockReset().mockResolvedValue({})
})

describe('BookingRulesSection — ARCHITECTURE_CYCLE32.md §32.8', () => {
  it('V32-14: body is exactly the rule keys', async () => {
    const u = userEvent.setup()
    renderIt(company())
    const h = screen.getByLabelText(H)
    await u.clear(h)
    await u.type(h, '30')
    await saveRules(u)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update.mock.calls[0]).toEqual(['co1', { bookingHorizonDays: 30, clientRescheduleMinHours: 2, allowSelfBooking: true, requirePrepayment: false }])
  })

  it('V32-15: caption verbatim and linked to the field, label present, h2 + note', () => {
    renderIt(company())
    expect(screen.getByText(CANCEL_WINDOW_FIELD_CAPTION)).toBeInTheDocument()
    expect(screen.getByLabelText(W)).toHaveAccessibleDescription(expect.stringContaining(CANCEL_WINDOW_FIELD_CAPTION))
    expect(screen.getByRole('heading', { level: 2, name: 'Правила записи' })).toBeInTheDocument()
    expect(screen.getByText(/Изменения действуют сразу/)).toBeInTheDocument()
  })

  it('V32-16: cleared window -> no key; cleared horizon -> 0; horizon 400 -> error, no request', async () => {
    const u = userEvent.setup()
    renderIt(company())
    await u.clear(screen.getByLabelText(W))
    await u.clear(screen.getByLabelText(H))
    await saveRules(u)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    const body = update.mock.calls[0][1]
    expect(body.bookingHorizonDays).toBe(0)
    expect(body).not.toHaveProperty('clientRescheduleMinHours')
    await u.type(screen.getByLabelText(H), '400')
    await saveRules(u)
    expect(await screen.findByText('Горизонт записи — от 1 до 365 дней')).toBeInTheDocument()
    expect(update).toHaveBeenCalledTimes(1)
  })

  it('V32-17: plan-locked checkboxes are disabled, warned and never sent', async () => {
    const u = userEvent.setup()
    renderIt(company({ planAllowsOnlineBooking: false, planAllowsOnlinePayment: false }))
    expect(screen.getByLabelText('Разрешить клиентам записываться самостоятельно')).toBeDisabled()
    expect(screen.getByLabelText('Требовать предоплату при онлайн-записи')).toBeDisabled()
    expect(screen.getByText(/Онлайн-запись не входит в текущий тариф/)).toBeInTheDocument()
    expect(screen.getByText(/Онлайн-оплата не входит в текущий тариф/)).toBeInTheDocument()
    await saveRules(u)
    await waitFor(() => expect(update).toHaveBeenCalled())
    expect(update.mock.calls[0][1]).not.toHaveProperty('allowSelfBooking')
    expect(update.mock.calls[0][1]).not.toHaveProperty('requirePrepayment')
  })

  it('V32-18: cards do not reset each other; 400 about the window goes to its field', async () => {
    const u = userEvent.setup()
    renderIt(company(), true)
    await u.type(screen.getByLabelText('Название *'), '!')
    await saveRules(u)
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(screen.getByLabelText('Название *')).toHaveValue('Салон!')
    const h = screen.getByLabelText(H)
    await u.clear(h)
    await u.type(h, '10')
    await u.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(2))
    expect(h).toHaveValue(10)

    failWith = err(400, 'Окно переноса — от 0 до 168 часов')
    await saveRules(u)
    await waitFor(() => expect(screen.getByLabelText(W)).toHaveAccessibleDescription(expect.stringContaining('Окно переноса — от 0 до 168 часов')))
  })
})
