import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MembersTab } from './CompanyManagePage'
import type { MemberDto } from '../../api/companies'

const getMembers = vi.fn()
const getByCompany = vi.fn()
const getMy = vi.fn()
const updateMemberProvidesServices = vi.fn()

vi.mock('../../api/companies', () => ({
  companiesApi: {
    getMembers: (...args: unknown[]) => getMembers(...args),
    getMy: (...args: unknown[]) => getMy(...args),
    updateMemberProvidesServices: (...args: unknown[]) => updateMemberProvidesServices(...args),
    updateMemberServices: vi.fn(),
    updateMemberCommission: vi.fn(),
    addMember: vi.fn(),
    removeMember: vi.fn(),
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
