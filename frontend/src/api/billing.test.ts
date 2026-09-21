import { describe, it, expect, vi } from 'vitest'
import { billingApi } from './billing'
import { api } from './client'

vi.mock('./client', () => ({
  api: { get: vi.fn(), post: vi.fn(), delete: vi.fn() },
}))

describe('billingApi.getSubscription', () => {
  it('resolves to the response body', async () => {
    const body = { currency: 'RUB', status: 'Active' }
    vi.mocked(api.get).mockResolvedValueOnce({ data: body })

    await expect(billingApi.getSubscription()).resolves.toEqual(body)
    expect(api.get).toHaveBeenCalledWith('/billing/subscription')
  })
})

describe('billingApi.submitRequest', () => {
  it('sends the full desired option composition, not a delta', async () => {
    const body = { id: '1', status: 'Pending' }
    vi.mocked(api.post).mockResolvedValueOnce({ data: body })

    await billingApi.submitRequest({ options: [{ optionId: 'opt-1', quantity: 2 }] })

    expect(api.post).toHaveBeenCalledWith('/billing/subscription/request', {
      options: [{ optionId: 'opt-1', quantity: 2 }],
    })
  })
})

describe('billingApi.cancelRequest', () => {
  it('resolves to undefined on 204', async () => {
    vi.mocked(api.delete).mockResolvedValueOnce({ data: undefined })

    await expect(billingApi.cancelRequest()).resolves.toBeUndefined()
    expect(api.delete).toHaveBeenCalledWith('/billing/subscription/request')
  })
})
