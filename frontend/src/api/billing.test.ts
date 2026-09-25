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

// Cycle 18 (API_CONTRACT_CYCLE18.md §362–§363.1)
describe('billingApi.getTrial', () => {
  it('resolves to the TrialStateDto body', async () => {
    const body = { state: 'Available', message: 'x', mailingWindow: { state: 'NotStarted', text: 'y' } }
    vi.mocked(api.get).mockResolvedValueOnce({ data: body })

    await expect(billingApi.getTrial()).resolves.toEqual(body)
    expect(api.get).toHaveBeenCalledWith('/billing/trial')
  })
})

describe('billingApi.activateTrial', () => {
  it('sends exactly {termsVersion} — no field the caller could use to ask for a different duration/составе', async () => {
    const body = { currency: 'RUB', status: 'Active' }
    vi.mocked(api.post).mockResolvedValueOnce({ data: body })

    await billingApi.activateTrial('2026-09-26')

    expect(api.post).toHaveBeenCalledWith('/billing/trial', { termsVersion: '2026-09-26' })
  })
})

describe('billingApi.acknowledgeTerms', () => {
  it('sends {termsVersion} to the acknowledgement route', async () => {
    const body = { state: 'Active', message: 'x', mailingWindow: { state: 'NotStarted', text: 'y' } }
    vi.mocked(api.post).mockResolvedValueOnce({ data: body })

    await billingApi.acknowledgeTerms('2026-09-26')

    expect(api.post).toHaveBeenCalledWith('/billing/trial/terms-acknowledgement', { termsVersion: '2026-09-26' })
  })
})
