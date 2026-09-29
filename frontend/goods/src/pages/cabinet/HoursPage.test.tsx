import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { HoursPage } from './HoursPage'
import type { ShopManageDto, WorkingHoursDto } from '../../types'

const workingHours = vi.fn()
const putWorkingHours = vi.fn()
const specialDays = vi.fn()
const putSpecialDay = vi.fn()
const deleteSpecialDay = vi.fn()
const putPickupSettings = vi.fn()
vi.mock('../../api/schedule', () => ({
  scheduleApi: {
    workingHours: (...a: unknown[]) => workingHours(...a),
    putWorkingHours: (...a: unknown[]) => putWorkingHours(...a),
    specialDays: (...a: unknown[]) => specialDays(...a),
    putSpecialDay: (...a: unknown[]) => putSpecialDay(...a),
    deleteSpecialDay: (...a: unknown[]) => deleteSpecialDay(...a),
    putPickupSettings: (...a: unknown[]) => putPickupSettings(...a),
  },
}))

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const
const hours = (over: Record<string, { start: string; end: string }[]> = {}, isSet = true): WorkingHoursDto => ({
  isSet,
  days: DAYS.map((d) => ({ dayOfWeek: d, dayLabel: d, text: '', intervals: (over[d] ?? []).map((i) => ({ ...i, crossesMidnight: i.end <= i.start })) })),
})

const shop = (over: Partial<ShopManageDto> = {}) =>
  ({
    id: 's1', name: 'Шаурма', myRole: 'Owner', acceptingOrders: false, notAcceptingReason: 'Задайте часы работы — без них магазин не принимает заказы',
    workingHoursSet: false, setupChecklist: [{ code: 'WorkingHours', text: 'Задайте часы работы', done: false }],
    pickupSettings: { asapEnabled: true, scheduledEnabled: true, slotStepMinutes: 15, preorderDays: 1, minPrepMinutes: 15 },
    settings: {}, ...over,
  }) as unknown as ShopManageDto

function renderPage(s = shop()) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/cabinet/s1/hours']}>
        <Routes>
          <Route element={<Outlet context={{ shop: s, isOwner: true }} />}>
            <Route path="/cabinet/:shopId/hours" element={<HoursPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  workingHours.mockReset().mockResolvedValue(hours({}, false))
  putWorkingHours.mockReset()
  specialDays.mockReset().mockResolvedValue([])
  putSpecialDay.mockReset()
  deleteSpecialDay.mockReset()
  putPickupSettings.mockReset().mockResolvedValue({})
})

describe('HoursPage', () => {
  it('shows the checklist and the owner reason while the hours are not set', async () => {
    renderPage()
    expect(await screen.findByTestId('setup-checklist')).toHaveTextContent('Чтобы начать принимать заказы:')
    expect(screen.getByText('Задайте часы работы')).toBeInTheDocument()
    expect(screen.getByText('Задайте часы работы — без них магазин не принимает заказы')).toBeInTheDocument()
  })

  it('hides the checklist once everything is done', async () => {
    renderPage(shop({ setupChecklist: [{ code: 'WorkingHours', text: 'Задайте часы работы', done: true }], acceptingOrders: true }))
    await screen.findByRole('heading', { name: 'Часы работы' })
    expect(screen.queryByTestId('setup-checklist')).toBeNull()
  })

  it('saves all seven days on the 5-minute grid, a day off as an empty list', async () => {
    putWorkingHours.mockImplementation(() => Promise.resolve(hours({ Monday: [{ start: '09:00', end: '21:00' }] })))
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Часы работы' })
    await user.click(screen.getAllByRole('button', { name: 'Задать часы' })[0]) // Monday: 09:00–21:00 default
    await user.click(screen.getByRole('button', { name: 'Сохранить часы' }))
    await waitFor(() => expect(putWorkingHours).toHaveBeenCalled())
    const body = putWorkingHours.mock.calls[0][1]
    expect(body.days).toHaveLength(7)
    expect(body.days[0]).toEqual({ dayOfWeek: 'Monday', intervals: [{ start: '09:00', end: '21:00' }] })
    expect(body.days[1]).toEqual({ dayOfWeek: 'Tuesday', intervals: [] })
    expect(await screen.findByText(/Сохранено. Изменения действуют на новые заказы/)).toBeInTheDocument()
  })

  it('prints the server text when the intervals overlap', async () => {
    putWorkingHours.mockImplementation(() => Promise.reject({ response: { status: 400, data: 'Интервалы должны идти по порядку и не пересекаться' } }))
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Часы работы' })
    await user.click(screen.getAllByRole('button', { name: 'Задать часы' })[0])
    await user.click(screen.getByRole('button', { name: 'Сохранить часы' }))
    expect(await screen.findByText('Интервалы должны идти по порядку и не пересекаться')).toBeInTheDocument()
  })

  it('refuses to save pick-up settings with both ways switched off', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Время получения' })
    await user.click(screen.getByRole('checkbox', { name: /Как можно скорее/ }))
    await user.click(screen.getByRole('checkbox', { name: /Заказ ко времени и предзаказ/ }))
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(await screen.findByText('Включите хотя бы один вариант времени получения')).toBeInTheDocument()
    expect(putPickupSettings).not.toHaveBeenCalled()
  })

  it('sends all five pick-up fields', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Время получения' })
    await user.selectOptions(screen.getByLabelText('Шаг слотов'), '30')
    await user.clear(screen.getByLabelText('Предзаказ, дней вперёд'))
    await user.type(screen.getByLabelText('Предзаказ, дней вперёд'), '3')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(putPickupSettings).toHaveBeenCalledWith('s1', { asapEnabled: true, scheduledEnabled: true, slotStepMinutes: 30, preorderDays: 3, minPrepMinutes: 15 }))
  })
})

describe('SpecialDays (P1)', () => {
  it('lists special days and removes one', async () => {
    specialDays.mockResolvedValue([{ date: '2026-10-05', label: 'пн 5 окт', isClosed: true, intervals: [], text: 'выходной' }])
    deleteSpecialDay.mockResolvedValue(undefined)
    const user = userEvent.setup()
    renderPage()
    expect(await screen.findByText(/пн 5 окт/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Вернуть обычные часы: пн 5 окт' }))
    expect(deleteSpecialDay).toHaveBeenCalledWith('s1', '2026-10-05')
  })

  it('on 409 ScheduleConflictsWithOrders shows the orders and repeats with confirmConflicts only after the owner agrees', async () => {
    const conflict = { response: { status: 409, data: { code: 'ScheduleConflictsWithOrders', message: 'На этот день уже есть заказы вне новых часов — свяжитесь с покупателями или отмените заказы', conflictingOrders: [{ orderId: 'o1', number: 5, pickupText: 'пт 2 окт, к 12:30', statusText: 'Принят', customerName: 'Иван', customerPhone: '79001234567' }] } } }
    putSpecialDay.mockImplementationOnce(() => Promise.reject(conflict)).mockResolvedValueOnce({ date: '2026-10-02' })
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('heading', { name: 'Особые дни' })
    await user.type(screen.getByLabelText('Дата'), '2026-10-02')
    await user.click(screen.getByRole('button', { name: 'Сохранить день' }))
    const dialog = await screen.findByRole('dialog', { name: 'На этот день уже есть заказы' })
    expect(within(dialog).getByTestId('conflict-message')).toHaveTextContent('свяжитесь с покупателями')
    expect(within(dialog).getByText(/№ 5/)).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: '+7 (900) 123-45-67' })).toHaveAttribute('href', 'tel:+79001234567')
    expect(putSpecialDay.mock.calls[0][2].confirmConflicts).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: 'Всё равно сохранить' }))
    await waitFor(() => expect(putSpecialDay).toHaveBeenCalledTimes(2))
    expect(putSpecialDay.mock.calls[1][2].confirmConflicts).toBe(true)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })
})
