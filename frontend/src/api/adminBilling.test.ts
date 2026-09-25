import { describe, it, expect, vi } from 'vitest'
import { adminBillingApi } from './adminBilling'
import { api } from './client'

vi.mock('./client', () => ({
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

// Cycle 18 (API_CONTRACT_CYCLE18.md §368)
describe('adminBillingApi.grantTrial', () => {
  it('POSTs body-less to the grant route', async () => {
    const body = { id: 'acc-1' }
    vi.mocked(api.post).mockResolvedValueOnce({ data: body })

    await expect(adminBillingApi.grantTrial('acc-1')).resolves.toEqual(body)
    expect(api.post).toHaveBeenCalledWith('/admin/billing-accounts/acc-1/trial')
  })
})

describe('adminBillingApi.regrantTrial', () => {
  it('sends the mandatory reason to the SEPARATE regrant route, not the ordinary grant route', async () => {
    const body = { id: 'acc-1' }
    vi.mocked(api.post).mockResolvedValueOnce({ data: body })

    await adminBillingApi.regrantTrial('acc-1', 'Сбой 12.09, исправляем свою ошибку')

    expect(api.post).toHaveBeenCalledWith('/admin/billing-accounts/acc-1/trial/regrant', {
      reason: 'Сбой 12.09, исправляем свою ошибку',
    })
  })
})

describe('adminBillingApi.listAccounts', () => {
  it('forwards the new ?trial= filter alongside the existing params', async () => {
    const body = { items: [], page: 1, pageSize: 20, totalCount: 0 }
    vi.mocked(api.get).mockResolvedValueOnce({ data: body })

    await adminBillingApi.listAccounts({ trial: 'active', page: 1, pageSize: 20 })

    expect(api.get).toHaveBeenCalledWith('/admin/billing-accounts', { params: { trial: 'active', page: 1, pageSize: 20 } })
  })
})
