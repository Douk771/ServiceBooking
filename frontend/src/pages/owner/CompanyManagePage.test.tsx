import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
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
    // address field's own save via AddressVerifyField, §209) — writing a new value into the SAME
    // query cache the form's `values` prop reads from triggers RHF's `keepDirtyValues` resync path,
    // exactly like a real refetch landing while the owner is still typing.
    qc.setQueryData(['my-companies'], [{ id: 'co1', name: 'Салон красоты', allowSelfBooking: false }])

    await waitFor(() => expect(nameInput).toHaveValue('Новое название'))
    expect(saveButton).not.toBeDisabled()
  })
})
