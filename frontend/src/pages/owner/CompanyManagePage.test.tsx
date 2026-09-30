import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MembersTab, SettingsTab } from './CompanyManagePage'
import type { MemberDto } from '../../api/companies'

const getMembers = vi.fn()
const getByCompany = vi.fn()
const getMy = vi.fn()
const updateMemberProvidesServices = vi.fn()
const update = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: {
    getMembers: (...args: unknown[]) => getMembers(...args),
    getMy: (...args: unknown[]) => getMy(...args),
    updateMemberProvidesServices: (...args: unknown[]) => updateMemberProvidesServices(...args),
    updateMemberServices: vi.fn(),
    updateMemberCommission: vi.fn(),
    addMember: vi.fn(),
    removeMember: vi.fn(),
    update: (...args: unknown[]) => update(...args),
    uploadLogo: vi.fn(),
  },
}))

const saveAddress = vi.fn()
vi.mock('../../api/companyAddress', () => ({
  companyAddressApi: { saveAddress: (...a: unknown[]) => saveAddress(...a), notice: vi.fn() },
}))
vi.mock('../../api/cities', () => ({ citiesApi: { search: () => Promise.resolve([]) } }))

vi.mock('../../api/services', () => ({
  servicesApi: { getByCompany: (...args: unknown[]) => getByCompany(...args) },
}))

function makeMember(overrides: Partial<MemberDto> = {}): MemberDto {
  return {
    id: 'm1',
    userId: 'u1',
    firstName: 'Анна',
    lastName: 'Петрова',
    phone: '79990000000',
    role: 'Master',
    serviceIds: [],
    commissionPercent: 0,
    providesServices: true,
    ...overrides,
  }
}

function renderWithProviders(companyId = 'co1') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <MembersTab companyId={companyId} />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  getMembers.mockReset().mockResolvedValue([makeMember()])
  getByCompany.mockReset().mockResolvedValue([])
  getMy.mockReset().mockResolvedValue([{ id: 'co1', maxEmployees: null }])
  updateMemberProvidesServices.mockReset()
})

describe('MembersTab — US-62 "provides services" toggle', () => {
  it('turns the flag off immediately when there are no future bookings', async () => {
    updateMemberProvidesServices.mockResolvedValue({})
    renderWithProviders()

    const checkbox = await screen.findByRole('checkbox', { name: 'Оказывает услуги' })
    expect(checkbox).toBeChecked()

    await userEvent.click(checkbox)

    await waitFor(() => expect(updateMemberProvidesServices).toHaveBeenCalledWith('co1', 'm1', false, false))
    expect(screen.queryByText(/будущие записи/)).not.toBeInTheDocument()
  })

  it('shows the server warning and lets the owner confirm turning it off with future bookings', async () => {
    updateMemberProvidesServices
      .mockRejectedValueOnce({
        isAxiosError: true,
        response: { status: 409, data: 'У специалиста 3 будущие записи. Они останутся в силе.' },
      })
      .mockResolvedValueOnce({})
    renderWithProviders()

    const checkbox = await screen.findByRole('checkbox', { name: 'Оказывает услуги' })
    await userEvent.click(checkbox)

    expect(await screen.findByText('У специалиста 3 будущие записи. Они останутся в силе.')).toBeInTheDocument()
    const confirmButton = screen.getByRole('button', { name: 'Всё равно выключить' })

    await userEvent.click(confirmButton)

    await waitFor(() => expect(updateMemberProvidesServices).toHaveBeenCalledWith('co1', 'm1', false, true))
  })

  it('lets the owner cancel instead of confirming', async () => {
    updateMemberProvidesServices.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 409, data: 'У специалиста 1 будущая запись.' },
    })
    renderWithProviders()

    const checkbox = await screen.findByRole('checkbox', { name: 'Оказывает услуги' })
    await userEvent.click(checkbox)

    expect(await screen.findByText('У специалиста 1 будущая запись.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Отмена' }))

    expect(screen.queryByText('У специалиста 1 будущая запись.')).not.toBeInTheDocument()
    // Still checked — the cancelled toggle never got persisted.
    expect(screen.getByRole('checkbox', { name: 'Оказывает услуги' })).toBeChecked()
  })
})

describe('SettingsTab — unsaved edits survive an unrelated `my-companies` refetch (review finding, cycle 13)', () => {
  function renderSettings(companyId = 'co1') {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const utils = render(
      <QueryClientProvider client={qc}>
        <SettingsTab companyId={companyId} />
      </QueryClientProvider>,
    )
    return { ...utils, qc }
  }

  beforeEach(() => {
    update.mockReset()
  })

  it('keeps typed-but-unsubmitted text and an enabled Save button after `values` resyncs to an unrelated field change', async () => {
    getMy.mockReset().mockResolvedValue([{ id: 'co1', name: 'Салон красоты', allowSelfBooking: true }])
    const { qc } = renderSettings()

    const nameInput = await screen.findByLabelText('Название')
    // Let the initial `values` resync settle before editing, so it can't race with the interactions
    // below and produce a flaky "typed text landed before/after a resync" result.
    await waitFor(() => expect(nameInput).toHaveValue('Салон красоты'))

    await userEvent.clear(nameInput)
    await userEvent.type(nameInput, 'Новое название')
    await waitFor(() => expect(nameInput).toHaveValue('Новое название'))

    const saveButton = screen.getByRole('button', { name: 'Сохранить изменения' })
    expect(saveButton).not.toBeDisabled()

    // Simulate `['my-companies']` resyncing mid-edit because of an unrelated change (e.g. the
    // address field's own save via CompanyAddressField, §209) — writing a new value into the SAME
    // query cache the form's `values` prop reads from triggers RHF's `keepDirtyValues` resync path,
    // exactly like a real refetch landing while the owner is still typing.
    qc.setQueryData(['my-companies'], [{ id: 'co1', name: 'Салон красоты', allowSelfBooking: false }])

    await waitFor(() => expect(nameInput).toHaveValue('Новое название'))
    expect(saveButton).not.toBeDisabled()
  })
})

// ARCHITECTURE_CYCLE17.md §305.3/§326 (US-17-03, C15-6.3): the hint text must match the ACTUAL "PUT
// with the field omitted" behaviour ("leave the saved value alone"), not the field's server-side
// default — and clearing the field must not send `clientRescheduleMinHours` in the body at all.
describe('SettingsTab — clientRescheduleMinHours hint and empty-field behaviour (§305.3, §326)', () => {
  function renderSettings(companyId = 'co1') {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <QueryClientProvider client={qc}>
        <SettingsTab companyId={companyId} />
      </QueryClientProvider>,
    )
  }

  beforeEach(() => {
    update.mockReset().mockResolvedValue({})
    getMy.mockReset().mockResolvedValue([
      { id: 'co1', name: 'Салон красоты', allowSelfBooking: true, clientRescheduleMinHours: 24 },
    ])
  })

  // Т20-05 п. 3 (API_CONTRACT_CYCLE20.md §435, US-20-04) replaced the §305.3 "leave the saved value
  // alone" hint with the lawyer's caption about the 24 h cancellation ceiling — dословно
  // `staffNotices.ts` (CANCEL_WINDOW_FIELD_CAPTION), not a paraphrase written at the call site.
  it('shows the Т20-05 п. 3 caption about the 24 h cancellation ceiling, not the old §305.3 copy', async () => {
    renderSettings()

    expect(await screen.findByText(/не больше 24 часов/)).toBeInTheDocument()
    expect(screen.queryByText(/Пусто — оставить текущее значение/)).not.toBeInTheDocument()
    expect(screen.queryByText('Пусто — 2 часа по умолчанию. 0 — можно перенести вплоть до начала визита.')).not.toBeInTheDocument()
  })

  it('label mentions both reschedule and cancellation, since one window now governs both (§304)', async () => {
    renderSettings()
    expect(await screen.findByLabelText('За сколько часов клиент может перенести или отменить запись')).toBeInTheDocument()
  })

  it('clearing the field omits clientRescheduleMinHours from the PUT body entirely', async () => {
    renderSettings()

    const hoursInput = await screen.findByLabelText('За сколько часов клиент может перенести или отменить запись')
    await waitFor(() => expect(hoursInput).toHaveValue(24))
    await userEvent.clear(hoursInput)

    await userEvent.click(screen.getByRole('button', { name: 'Сохранить изменения' }))

    await waitFor(() => expect(update).toHaveBeenCalled())
    const body = update.mock.calls[0][1] as Record<string, unknown>
    // `undefined` (not sent by JSON.stringify, which is what actually crosses the wire) rather than
    // an empty string/0 — an empty string would be a stray no-op to the API contract, 0 is a
    // legitimate explicit value the owner didn't type.
    expect(body.clientRescheduleMinHours).toBeUndefined()
  })
})

// ARCHITECTURE_CYCLE29.md §29.9 (US-29-01): the four field groups and the city block inside the main form.
describe('SettingsTab — cycle 29 field groups', () => {
  function renderSettings() {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <QueryClientProvider client={qc}>
        <SettingsTab companyId="co1" />
      </QueryClientProvider>,
    )
  }
  const COMPANY = {
    id: 'co1', name: 'Салон', address: 'Ленина, 1', cityId: 5, cityName: 'Барнаул', cityRegion: 'Алтайский край',
    timeZoneId: 'Asia/Barnaul', utcOffsetMinutes: 420, allowSelfBooking: true, clientRescheduleMinHours: 2,
    planAllowsOnlineBooking: true, planAllowsOnlinePayment: true, planAllowsPublicListing: true,
  }

  beforeEach(() => {
    update.mockReset().mockResolvedValue({})
    saveAddress.mockReset()
    getMy.mockReset().mockResolvedValue([COMPANY])
  })

  it('V29-01: fields live in their own named groups', async () => {
    renderSettings()
    await screen.findByRole('group', { name: 'Город и часовой пояс' })
    const main = screen.getByRole('group', { name: 'Основное' })
    expect(within(main).getByLabelText('Название')).toBeInTheDocument()
    expect(within(main).getByText('Описание')).toBeInTheDocument()
    const contacts = screen.getByRole('group', { name: 'Контакты' })
    expect(within(contacts).getByLabelText('Телефон')).toBeInTheDocument()
    expect(within(contacts).getByLabelText('Email')).toBeInTheDocument()
    const addr = screen.getByRole('group', { name: 'Адрес и карты' })
    expect(within(addr).getByRole('group', { name: 'Город и часовой пояс' })).toBeInTheDocument()
    expect(within(addr).getByLabelText('Ссылка на Яндекс Картах')).toBeInTheDocument()
    expect(within(addr).getByLabelText('Ссылка на 2ГИС')).toBeInTheDocument()
    const rec = screen.getByRole('group', { name: 'Запись' })
    expect(within(rec).getByLabelText('На сколько дней вперёд клиент может записаться')).toBeInTheDocument()
    expect(within(rec).getByLabelText('За сколько часов клиент может перенести или отменить запись')).toBeInTheDocument()
    expect(within(rec).getAllByRole('checkbox')).toHaveLength(3)
  })

  it('V29-02: main save sends one update without cityId/timeZoneId/address', async () => {
    renderSettings()
    const name = await screen.findByLabelText('Название')
    await waitFor(() => expect(name).toHaveValue('Салон'))
    await userEvent.type(name, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить изменения' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    const body = update.mock.calls[0][1] as Record<string, unknown>
    expect(body).not.toHaveProperty('cityId')
    expect(body).not.toHaveProperty('timeZoneId')
    expect(body).not.toHaveProperty('address')
    expect(saveAddress).not.toHaveBeenCalled()
  })

  it('V29-03: city save sends exactly { cityId, timeZoneId } and does not submit the main form', async () => {
    renderSettings()
    const city = await screen.findByRole('group', { name: 'Город и часовой пояс' })
    await waitFor(() => expect(within(city).getByRole('combobox')).toHaveValue('Барнаул, Алтайский край'))
    await userEvent.click(within(city).getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    expect(update).toHaveBeenCalledWith('co1', { cityId: 5, timeZoneId: null })
  })

  it('V29-04: Enter in the city and IANA fields does not submit anything', async () => {
    renderSettings()
    const city = await screen.findByRole('group', { name: 'Город и часовой пояс' })
    await userEvent.type(within(city).getByRole('combobox'), '{Enter}')
    await userEvent.click(within(city).getByRole('checkbox'))
    await userEvent.type(within(city).getByPlaceholderText('Asia/Barnaul'), '{Enter}')
    expect(update).not.toHaveBeenCalled()
  })

  it('V29-06: a 400 about the Yandex link shows next to its field inside the address group', async () => {
    update.mockRejectedValue({ response: { status: 400, data: 'Ждём ссылку на Яндекс Карты — например, https://yandex.ru/maps/org/1' } })
    renderSettings()
    const name = await screen.findByLabelText('Название')
    await waitFor(() => expect(name).toHaveValue('Салон'))
    await userEvent.type(name, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить изменения' }))
    const addr = screen.getByRole('group', { name: 'Адрес и карты' })
    const field = await within(addr).findByLabelText('Ссылка на Яндекс Картах')
    await waitFor(() => expect(field).toHaveAttribute('aria-describedby', 'yandexMapsUrl-error'))
    expect(within(addr).getByText(/Ждём ссылку на Яндекс/)).toBeInTheDocument()
  })
})
