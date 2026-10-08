import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ArrivalReminderCard } from './ArrivalReminderCard'
import { httpError } from '../../test/fixtures'
import type { ArrivalReminderPreviewDto, ArrivalReminderSettingsDto } from '../../types'

const api = vi.hoisted(() => ({ arrivalReminder: vi.fn(), saveArrivalReminder: vi.fn(), previewArrivalReminder: vi.fn(), arrivalReminderHistory: vi.fn() }))
vi.mock('../../api/staysCompanies', () => ({ staysCompaniesApi: api }))
vi.mock('../../api/legalTexts', () => ({ stayLegalTextsApi: { get: () => Promise.reject(httpError(404, '')) } }))

const DEFAULT = '{Компания}: завтра заезд в «{Дом}» — {ДатаЗаезда} с {ВремяЗаезда}.\nБронь: {СсылкаНаБронь}'
const settings = (over: Partial<ArrivalReminderSettingsDto> = {}): ArrivalReminderSettingsDto => ({
  enabled: true,
  time: '18:00',
  template: null,
  isDefault: true,
  effectiveTemplate: DEFAULT,
  defaultTemplate: DEFAULT,
  pushTextEnabled: false,
  placeholders: [
    { token: '{Дом}', description: 'Название дома', inMessenger: true, onPage: true, inPush: true },
    { token: '{ИмяГостя}', description: 'Имя гостя', inMessenger: true, onPage: true, inPush: false },
  ],
  limits: { templateMaxLength: 700, messengerMaxLength: 1000, pageMaxLength: 1000, pushMaxLength: 180 },
  ownerNotice: { key: 'StayReminderTemplateOwnerNotice', version: 'v2' },
  pushNotice: { key: 'StayReminderPushOwnerNotice', version: 'v3' },
  warnings: [],
  ...over,
})
const preview = (over: Partial<ArrivalReminderPreviewDto> = {}): ArrivalReminderPreviewDto => ({
  messenger: { text: 'Кедр Парк: завтра заезд', length: 23 },
  page: { text: 'Кедр Парк: завтра заезд', length: 23 },
  push: { text: 'Завтра заезд — откройте бронь', length: 29, usesFixedText: true, dropped: [] },
  warnings: [],
  errors: [],
  confirmationRequired: false,
  markers: [],
  ...over,
})

function renderCard() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ArrivalReminderCard companyId="c1" />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset())
  api.previewArrivalReminder.mockResolvedValue(preview())
  api.arrivalReminderHistory.mockResolvedValue([])
})

describe('ArrivalReminderCard', () => {
  it('shows the default template, the owner notice, the placeholder buttons and the three channels', async () => {
    api.arrivalReminder.mockResolvedValue(settings())
    renderCard()
    const area = (await screen.findByLabelText('Текст напоминания')) as HTMLTextAreaElement
    expect(area.value).toBe(DEFAULT)
    expect(screen.getByRole('button', { name: /Вставить \{Дом\}/ })).toBeInTheDocument()
    expect(screen.getByText(/за его содержание отвечаете вы/)).toBeInTheDocument()
    expect(await screen.findByLabelText('Предпросмотр: Мессенджер')).toBeInTheDocument()
    expect(screen.getByLabelText('Предпросмотр: Страница брони')).toBeInTheDocument()
    expect(screen.getByLabelText('Предпросмотр: Push')).toHaveTextContent('Завтра заезд — откройте бронь')
  })

  it('a changed text is sent as typed; the default is sent as null', async () => {
    api.arrivalReminder.mockResolvedValue(settings())
    api.saveArrivalReminder.mockResolvedValue(settings({ template: 'Ждём вас!', isDefault: false, effectiveTemplate: 'Ждём вас!' }))
    renderCard()
    const area = await screen.findByLabelText('Текст напоминания')
    fireEvent.change(area, { target: { value: 'Ждём вас!' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить напоминание' }))
    await waitFor(() => expect(api.saveArrivalReminder).toHaveBeenCalled())
    expect(api.saveArrivalReminder.mock.calls[0][1]).toMatchObject({ time: '18:00', template: 'Ждём вас!', pushTextEnabled: false, confirmCodeMarkers: false, ownerNoticeVersion: 'v2', pushNoticeVersion: null })
  })

  it('signs of an access code open a dialog; «Сохранить всё равно» repeats the save with the confirmation (ЮР39-4)', async () => {
    api.arrivalReminder.mockResolvedValue(settings())
    api.saveArrivalReminder
      .mockRejectedValueOnce(httpError(409, { code: 'ReminderConfirmationRequired', message: 'В тексте есть похожее на код доступа: 4512. Коды по умолчанию отправляются только ссылкой. Сохранить всё равно?', markers: ['4512'] }))
      .mockResolvedValueOnce(settings({ template: 'Код калитки 4512', isDefault: false }))
    renderCard()
    fireEvent.change(await screen.findByLabelText('Текст напоминания'), { target: { value: 'Код калитки 4512' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить напоминание' }))
    const dialog = await screen.findByTestId('markers-dialog')
    expect(dialog).toHaveTextContent('похожее на код доступа')
    expect(dialog).toHaveTextContent('4512')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Сохранить всё равно' }))
    await waitFor(() => expect(api.saveArrivalReminder).toHaveBeenCalledTimes(2))
    expect(api.saveArrivalReminder.mock.calls[1][1]).toMatchObject({ confirmCodeMarkers: true })
    await waitFor(() => expect(screen.queryByTestId('markers-dialog')).not.toBeInTheDocument())
  })

  it('«Вернуться к тексту» closes the dialog without a second request', async () => {
    api.arrivalReminder.mockResolvedValue(settings())
    api.saveArrivalReminder.mockRejectedValue(httpError(409, { code: 'ReminderConfirmationRequired', message: 'Похоже на код', markers: ['4512'] }))
    renderCard()
    fireEvent.change(await screen.findByLabelText('Текст напоминания'), { target: { value: 'Код 4512' } })
    fireEvent.click(screen.getByRole('button', { name: 'Сохранить напоминание' }))
    fireEvent.click(await within(await screen.findByTestId('markers-dialog')).findByRole('button', { name: 'Вернуться к тексту' }))
    await waitFor(() => expect(screen.queryByTestId('markers-dialog')).not.toBeInTheDocument())
    expect(api.saveArrivalReminder).toHaveBeenCalledTimes(1)
  })

  it('turning on the text in push needs the acknowledgement; it goes with the version of the shown notice (ЮР39-3)', async () => {
    api.arrivalReminder.mockResolvedValue(settings())
    api.saveArrivalReminder.mockResolvedValue(settings({ pushTextEnabled: true }))
    renderCard()
    fireEvent.click(await screen.findByRole('switch', { name: /Показывать текст напоминания в push/ }))
    const save = screen.getByRole('button', { name: 'Сохранить напоминание' })
    expect(save).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Я прочитал предупреждение о push' }))
    expect(save).toBeEnabled()
    fireEvent.click(save)
    await waitFor(() => expect(api.saveArrivalReminder).toHaveBeenCalled())
    expect(api.saveArrivalReminder.mock.calls[0][1]).toMatchObject({ pushTextEnabled: true, pushNoticeVersion: 'v3' })
  })

  it('names the lines that drop out of the push with the reason, and blocks the save on a preview error', async () => {
    api.arrivalReminder.mockResolvedValue(settings({ pushTextEnabled: true }))
    api.previewArrivalReminder.mockResolvedValue(
      preview({
        push: { text: 'Ждём вас', length: 8, usesFixedText: false, dropped: [{ line: 2, text: 'Код калитки 4512', reason: 'Digits' }] },
        errors: [{ code: 'ForbiddenWords', message: 'Не используйте слова «задаток», «невозвратный», «депозит»' }],
      }),
    )
    renderCard()
    const dropped = await screen.findByTestId('push-dropped')
    expect(dropped).toHaveTextContent('Строка 2 «Код калитки 4512» не попадёт в push: 4 и больше цифр подряд')
    expect(await screen.findByText(/Не используйте слова/)).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Текст напоминания'), { target: { value: 'Задаток не возвращается' } })
    expect(screen.getByRole('button', { name: 'Сохранить напоминание' })).toBeDisabled()
  })

  it('«Вернуть по умолчанию» restores the default text', async () => {
    api.arrivalReminder.mockResolvedValue(settings({ template: 'Ждём вас!', isDefault: false, effectiveTemplate: 'Ждём вас!' }))
    renderCard()
    const area = (await screen.findByLabelText('Текст напоминания')) as HTMLTextAreaElement
    expect(area.value).toBe('Ждём вас!')
    fireEvent.click(screen.getByRole('button', { name: 'Вернуть по умолчанию' }))
    expect(area.value).toBe(DEFAULT)
  })

  it('shows an error with a retry when the settings cannot be loaded', async () => {
    api.arrivalReminder.mockRejectedValue(httpError(500, ''))
    renderCard()
    expect(await screen.findByRole('alert')).toHaveTextContent('Сервер временно недоступен')
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeInTheDocument()
  })
})
