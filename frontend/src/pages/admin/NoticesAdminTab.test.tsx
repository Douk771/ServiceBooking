import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NoticesAdminTab } from './NoticesAdminTab'
import type { AdminPlatformNoticeDto } from '../../api/adminNotices'

const list = vi.fn()
const publish = vi.fn()
const preview = vi.fn()
const revoke = vi.fn()
const getAttachment = vi.fn()

vi.mock('../../api/adminNotices', () => ({
  adminNoticesApi: {
    list: (...args: unknown[]) => list(...args),
    publish: (...args: unknown[]) => publish(...args),
    preview: (...args: unknown[]) => preview(...args),
    revoke: (...args: unknown[]) => revoke(...args),
    getAttachment: (...args: unknown[]) => getAttachment(...args),
  },
}))

const listAccounts = vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 5, totalCount: 0 })
vi.mock('../../api/adminBilling', () => ({
  adminBillingApi: { listAccounts: (...args: unknown[]) => listAccounts(...args) },
}))

const listPlans = vi.fn().mockResolvedValue([])
vi.mock('../../api/plans', () => ({
  plansApi: { list: (...args: unknown[]) => listPlans(...args) },
}))

function adminNotice(overrides: Partial<AdminPlatformNoticeDto> = {}): AdminPlatformNoticeDto {
  return {
    id: 'n1',
    kind: 'Other',
    title: 'Плановый перерыв',
    body: 'Текст',
    linkUrl: null,
    effectiveFrom: null,
    publishedAt: '2026-09-29T09:00:00Z',
    visibleUntil: '2027-09-29T09:00:00Z',
    attachment: null,
    audienceType: 'AllOwners',
    audiencePlanIds: null,
    targetBillingAccountId: null,
    templateVersion: null,
    createdByName: 'Суперадмин',
    revokedAt: null,
    revokedByName: null,
    revokeReason: null,
    audienceCount: 12,
    acknowledgedCount: 3,
    ...overrides,
  } as AdminPlatformNoticeDto
}

function renderTab() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <NoticesAdminTab />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  list.mockReset()
  publish.mockReset()
  preview.mockReset()
  revoke.mockReset()
  getAttachment.mockReset()
})

// API_CONTRACT_CYCLE20.md §434.4–§434.7 (US-20-03).
describe('NoticesAdminTab', () => {
  it('shows the addressee/acknowledged counters from the server', async () => {
    list.mockResolvedValueOnce({ items: [adminNotice()], page: 1, pageSize: 20, total: 1, hasNext: false })
    renderTab()
    expect(await screen.findByText(/адресатов 12/)).toBeInTheDocument()
    expect(screen.getByText(/прочитали 3/)).toBeInTheDocument()
  })

  it('shows an empty state with no technical text', async () => {
    list.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })
    renderTab()
    expect(await screen.findByText('Уведомлений пока нет')).toBeInTheDocument()
  })

  it('previews before publishing, using the same validator (matrix §434.5)', async () => {
    const user = userEvent.setup()
    list.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })
    preview.mockResolvedValueOnce({ title: 'Заголовок', body: 'Тело', templateVersion: null, effectiveFrom: '2026-11-01', visibleUntil: '2027-01-01T00:00:00Z', audienceCount: 42, attachmentSha256: null })
    renderTab()

    await user.click(await screen.findByRole('button', { name: /Опубликовать уведомление/ }))
    await user.click(screen.getByRole('button', { name: 'Предпросмотр' }))

    expect(await screen.findByText('Адресатов сейчас: 42')).toBeInTheDocument()
    expect(publish).not.toHaveBeenCalled()
  })

  it('publishing a PriceChange sends the priceChange params and the chosen effectiveFrom', async () => {
    const user = userEvent.setup()
    list.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })
    publish.mockResolvedValueOnce(adminNotice({ kind: 'PriceChange' }))
    renderTab()

    await user.click(await screen.findByRole('button', { name: /Опубликовать уведомление/ }))
    // Default kind is already PriceChange.
    await user.type(screen.getByPlaceholderText('ID тарифа'), 'plan-1')
    await user.type(screen.getByPlaceholderText('Старая цена'), '990')
    await user.type(screen.getByPlaceholderText('Новая цена'), '1190')
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement
    await user.type(dateInput, '2026-11-01')
    await user.click(screen.getByRole('button', { name: 'Опубликовать' }))

    await waitFor(() => expect(publish).toHaveBeenCalledTimes(1))
    const body = publish.mock.calls[0][0]
    expect(body.kind).toBe('PriceChange')
    expect(body.priceChange).toEqual({ planId: 'plan-1', oldPricePerMonth: 990, newPricePerMonth: 1190 })
    expect(body.effectiveFrom).toBe('2026-11-01')
    expect(body.title).toBeNull()
    expect(body.body).toBeNull()
  })

  it('shows the server validation message verbatim on a failed publish', async () => {
    const user = userEvent.setup()
    list.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0, hasNext: false })
    publish.mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: 'effectiveFrom должна быть не раньше чем через 30 дней.' } })
    renderTab()

    await user.click(await screen.findByRole('button', { name: /Опубликовать уведомление/ }))
    await user.click(screen.getByRole('button', { name: 'Опубликовать' }))

    expect(await screen.findByText('effectiveFrom должна быть не раньше чем через 30 дней.')).toBeInTheDocument()
  })

  it('revoking requires a non-empty reason and calls the API with it', async () => {
    const user = userEvent.setup()
    list.mockResolvedValue({ items: [adminNotice()], page: 1, pageSize: 20, total: 1, hasNext: false })
    revoke.mockResolvedValueOnce(adminNotice({ revokedAt: '2026-09-30T00:00:00Z' }))
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Отозвать' }))
    const heading = await screen.findByText('Отозвать — Плановый перерыв')
    const modal = heading.closest('.bg-cream') as HTMLElement
    const confirmButton = within(modal).getByRole('button', { name: 'Отозвать' })
    expect(confirmButton).toBeDisabled()

    await user.type(within(modal).getByPlaceholderText('Причина отзыва'), 'Ошибка в дате')
    await user.click(confirmButton)

    await waitFor(() => expect(revoke).toHaveBeenCalledWith('n1', 'Ошибка в дате'))
  })

  it('does not show a revoke button for an already-revoked notice', async () => {
    list.mockResolvedValueOnce({ items: [adminNotice({ revokedAt: '2026-09-30T00:00:00Z' })], page: 1, pageSize: 20, total: 1, hasNext: false })
    renderTab()
    await screen.findByText('Отозвано')
    expect(screen.queryByRole('button', { name: 'Отозвать' })).not.toBeInTheDocument()
  })
})
