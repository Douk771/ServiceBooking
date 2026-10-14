import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BookingRulesCard } from './BookingRulesCard'
import type { BathsCompanyManageDto, BathsSettingsDto } from '../../cabinet/types'

/** A failed request as axios reports it: a real Error that carries the response. */
const httpError = (status: number, data: unknown = '') => Object.assign(new Error('http'), { response: { status, data } })

const updateSettings = vi.fn()
vi.mock('../../api/bathsCabinet', () => ({ bathsCabinetApi: { updateSettings: (...a: unknown[]) => updateSettings(...a) } }))

const company = { id: 'c1' } as BathsCompanyManageDto
const settings = (over: Partial<BathsSettingsDto> = {}): BathsSettingsDto => ({
  horizonDays: 90,
  holdMinutes: 60,
  housekeeperSeesGuestComment: false,
  showInCatalog: true,
  sessionReminderEnabled: false,
  sessionReminderHours: 0,
  ...over,
})

const renderCard = (s: BathsSettingsDto, onSaved = () => undefined) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      <BookingRulesCard company={company} settings={s} onSaved={onSaved} />
    </QueryClientProvider>,
  )

describe('BookingRulesCard reminder', () => {
  beforeEach(() => {
    updateSettings.mockReset()
  })

  it('hides the hours while the reminder is off (the server keeps no number then)', () => {
    renderCard(settings())
    expect(screen.getByRole('switch', { name: 'Напоминать о сеансе' })).toHaveAttribute('aria-checked', 'false')
    expect(screen.queryByLabelText('За сколько часов до начала')).toBeNull()
  })

  it('shows the hours, starting from 3, once the reminder is switched on', async () => {
    renderCard(settings())
    await userEvent.click(screen.getByRole('switch', { name: 'Напоминать о сеансе' }))
    expect(screen.getByLabelText('За сколько часов до начала')).toHaveValue(3)
  })

  it('keeps the hours the server sent while the reminder is on, and sends them', async () => {
    updateSettings.mockResolvedValue({})
    const onSaved = vi.fn()
    renderCard(settings({ sessionReminderEnabled: true, sessionReminderHours: 5 }), onSaved)
    expect(screen.getByLabelText('За сколько часов до начала')).toHaveValue(5)
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить настройки' }))
    await waitFor(() => expect(updateSettings).toHaveBeenCalled())
    expect(updateSettings).toHaveBeenCalledWith('c1', expect.objectContaining({ sessionReminderEnabled: true, sessionReminderHours: 5 }))
    await waitFor(() => expect(onSaved).toHaveBeenCalled())
  })

  it('saves a switched-off reminder with the default hours only to fit the shape', async () => {
    updateSettings.mockResolvedValue({})
    renderCard(settings({ sessionReminderEnabled: true, sessionReminderHours: 7 }))
    await userEvent.click(screen.getByRole('switch', { name: 'Напоминать о сеансе' }))
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить настройки' }))
    await waitFor(() => expect(updateSettings).toHaveBeenCalled())
    expect(updateSettings).toHaveBeenCalledWith('c1', expect.objectContaining({ sessionReminderEnabled: false, sessionReminderHours: 3 }))
  })

  it('does not send a form with a horizon out of range and says why', async () => {
    renderCard(settings({ horizonDays: 10 }))
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить настройки' }))
    expect(await screen.findByText('Горизонт бронирования — от 30 до 730 дней')).toBeInTheDocument()
    expect(updateSettings).not.toHaveBeenCalled()
  })

  it('puts a server 400 under its field', async () => {
    updateSettings.mockRejectedValue(httpError(400, 'Время на оплату — от 10 до 180 минут'))
    renderCard(settings())
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить настройки' }))
    expect(await screen.findByText('Время на оплату — от 10 до 180 минут')).toBeInTheDocument()
  })

  it('shows an error about hours in the form when the hours field is hidden', async () => {
    updateSettings.mockRejectedValue(httpError(400, 'Напоминание — за 1…24 часа до начала'))
    renderCard(settings())
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить настройки' }))
    expect(await screen.findByText('Напоминание — за 1…24 часа до начала')).toBeInTheDocument()
  })
})
