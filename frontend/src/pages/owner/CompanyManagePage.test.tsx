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
const getPhotoUsage = vi.fn()

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
    getPhotoUsage: (...args: unknown[]) => getPhotoUsage(...args),
  },
}))

const saveAddress = vi.fn()
vi.mock('../../api/companyAddress', () => ({
  companyAddressApi: { saveAddress: (...a: unknown[]) => saveAddress(...a), notice: vi.fn() },
}))
vi.mock('../../api/companyCatalogListing', () => ({
  companyCatalogListingApi: {
    get: () =>
      Promise.resolve({
        showInCatalog: true, allowedByPlan: true, visible: true, statusText: 'Салон виден в каталоге ezbook.ru',
        notAllowedByPlanText: null, checklist: [{ code: 'HiddenByOwner', text: 'Показ включен в настройках', done: true }],
      }),
    put: vi.fn(),
  },
}))
vi.mock('../../api/companyPhotos', () => ({ companyPhotosApi: { list: () => Promise.resolve([]) } }))
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

// ARCHITECTURE_CYCLE32.md §32.9 — the settings tab is six cards in one column; the old form tests moved to
// SalonProfileSection.test.tsx / BookingRulesSection.test.tsx (table §32.12.2).
describe('SettingsTab — cycle 32 layout', () => {
  function renderSettings() {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <QueryClientProvider client={qc}>
        <SettingsTab companyId="co1" />
      </QueryClientProvider>,
    )
  }
  const COMPANY = {
    id: 'co1', name: 'Салон', slug: 'salon', address: 'Ленина, 1', cityId: 5, cityName: 'Барнаул', cityRegion: 'Алтайский край',
    timeZoneId: 'Asia/Barnaul', utcOffsetMinutes: 420, allowSelfBooking: true, clientRescheduleMinHours: 2,
    planAllowsOnlineBooking: true, planAllowsOnlinePayment: true, planAllowsPublicListing: true,
  }

  beforeEach(() => {
    update.mockReset().mockResolvedValue({})
    saveAddress.mockReset()
    getMy.mockReset().mockResolvedValue([COMPANY])
    getPhotoUsage.mockReset().mockResolvedValue({ companyId: 'co1', usedBytes: 0, photoCount: 0, quotaMb: 100, percentUsed: 0, retention: 'SixMonths' })
  })

  it('V32-19: h2 order, no h3, no old title, column classes', async () => {
    const { container } = renderSettings()
    await screen.findByRole('heading', { name: 'Хранилище фото клиентов' })
    expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual([
      'Профиль салона', 'Фотографии салона', 'Правила записи', 'Каталог ezbook.ru', 'Виджет для сайта', 'Хранилище фото клиентов',
    ])
    expect(screen.queryAllByRole('heading', { level: 3 })).toHaveLength(0)
    expect(screen.queryByText('Настройки компании')).not.toBeInTheDocument()
    expect(container.firstElementChild).toHaveClass('max-w-[760px]', 'gap-5')
  })

  it('V32-19: old field groups are gone; address group lives in the profile', async () => {
    renderSettings()
    await screen.findByRole('group', { name: 'Адрес и карты' })
    for (const g of ['Основное', 'Контакты', 'Запись', 'Город и часовой пояс']) expect(screen.queryByRole('group', { name: g })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Город и часовой пояс' })).not.toBeInTheDocument()
  })

  it('V32-20: no "общий список" checkbox, catalog switch present, showInPublicListing never sent', async () => {
    getMy.mockResolvedValue([{ ...COMPANY, showInPublicListing: true }])
    renderSettings()
    const name = await screen.findByLabelText('Название *')
    expect(screen.queryByLabelText('Показывать компанию в общем списке')).not.toBeInTheDocument()
    expect(await screen.findByRole('switch', { name: 'Показывать салон в каталоге ezbook.ru' })).toBeInTheDocument()
    await userEvent.type(name, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить правила' }))
    await waitFor(() => expect(update).toHaveBeenCalledTimes(2))
    for (const call of update.mock.calls) expect(call[1]).not.toHaveProperty('showInPublicListing')
  })

  it('V32-21: Enter in the IANA field saves the profile only', async () => {
    renderSettings()
    await userEvent.click(await screen.findByLabelText(/Указать часовой пояс вручную/))
    await userEvent.type(screen.getByLabelText('Часовой пояс (IANA)'), 'Asia/Barnaul{Enter}')
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1))
    const body = update.mock.calls[0][1] as Record<string, unknown>
    expect(body).toHaveProperty('name')
    expect(body).not.toHaveProperty('bookingHorizonDays')
  })

  it('V32-22: a failed photo-usage card is not rendered; the widget is the last card', async () => {
    getPhotoUsage.mockReset().mockRejectedValue(new Error('x'))
    const { container } = renderSettings()
    await screen.findByRole('heading', { name: 'Виджет для сайта' })
    await waitFor(() => expect(getPhotoUsage).toHaveBeenCalled())
    expect(screen.queryByRole('heading', { name: 'Хранилище фото клиентов' })).not.toBeInTheDocument()
    const last = container.firstElementChild?.lastElementChild
    expect(within(last as HTMLElement).getByRole('heading', { name: 'Виджет для сайта' })).toBeInTheDocument()
  })

  it('shows a skeleton while loading and no profile/rules cards for a foreign company', async () => {
    getMy.mockReset().mockResolvedValue([])
    renderSettings()
    await waitFor(() => expect(getMy).toHaveBeenCalled())
    await screen.findByRole('heading', { name: 'Фотографии салона' })
    expect(screen.queryByRole('heading', { name: 'Профиль салона' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Правила записи' })).not.toBeInTheDocument()
  })
})
