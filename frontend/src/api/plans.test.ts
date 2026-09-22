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
