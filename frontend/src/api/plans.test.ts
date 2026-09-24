import { describe, it, expect, vi } from 'vitest'
import { plansApi } from './plans'
import { api } from './client'

vi.mock('./client', () => ({
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

describe('plansApi.setSystemFree', () => {
  it('PUTs to the dedicated system-free route, not the ordinary plan-edit route', async () => {
    const body = { id: 'p1', isSystemFree: true }
    vi.mocked(api.put).mockResolvedValueOnce({ data: body })

    await plansApi.setSystemFree('p1', true)

    expect(api.put).toHaveBeenCalledWith('/admin/plans/p1/system-free', { isSystemFree: true })
  })
})

describe('plansApi.list', () => {
  // Cycle 7 wrapped this response in a { plans } envelope to match the contract
  // (contracts/cycle7/openapi.yaml — required: [plans]) but the client kept asking for a bare array,
  // so PlansTab crashed on `plans.filter is not a function` the first time the release reached a
  // machine. The sibling listOptions envelope had a test; this one did not.
  it('unwraps the { plans } envelope', async () => {
    const plans = [{ id: 'p1', name: 'Базовый' }]
    vi.mocked(api.get).mockResolvedValueOnce({ data: { plans } })

    await expect(plansApi.list()).resolves.toEqual(plans)
    expect(api.get).toHaveBeenCalledWith('/admin/plans')
  })
})

describe('plansApi.listOptions', () => {
  it('unwraps the { options } envelope', async () => {
    const options = [{ id: 'o1', name: 'Опция' }]
    vi.mocked(api.get).mockResolvedValueOnce({ data: { options } })

    await expect(plansApi.listOptions()).resolves.toEqual(options)
    expect(api.get).toHaveBeenCalledWith('/admin/options')
  })
})

describe('plansApi.update', () => {
  it('sends highlights and the option-availability matrix on an ordinary edit', async () => {
    const body = { id: 'p1' }
    vi.mocked(api.put).mockResolvedValueOnce({ data: body })

    await plansApi.update('p1', {
      name: 'Basic',
      pricePerMonth: 990,
      allowOnlineBooking: true,
      allowMailing: false,
      allowAnalytics: false,
      allowPublicListing: true,
      allowOnlinePayment: false,
      notifyDaysBefore: 7,
      isPublic: true,
      isActive: true,
      sortOrder: 0,
      highlights: ['Онлайн-запись'],
      options: [{ optionId: 'opt-1', availability: 'Included', includedQuantity: null }],
    })

    expect(api.put).toHaveBeenCalledWith(
      '/admin/plans/p1',
      expect.objectContaining({
        highlights: ['Онлайн-запись'],
        options: [{ optionId: 'opt-1', availability: 'Included', includedQuantity: null }],
      }),
    )
  })
})
