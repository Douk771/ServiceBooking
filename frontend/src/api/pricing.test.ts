import { describe, it, expect, vi } from 'vitest'
import { AxiosError } from 'axios'
import { pricingApi } from './pricing'
import { api } from './client'

vi.mock('./client', () => ({
  api: { get: vi.fn() },
}))

describe('pricingApi.getPublicPricing', () => {
  it('resolves to the response body on success', async () => {
    const body = { version: 'v1', currency: 'RUB', plans: [], options: [], notice: 'x' }
    vi.mocked(api.get).mockResolvedValueOnce({ data: body })

    await expect(pricingApi.getPublicPricing()).resolves.toEqual(body)
  })

  it('maps a 404 (publication switched off) to null instead of throwing', async () => {
    const err = new AxiosError('Not Found', '404', undefined, undefined, {
      status: 404,
      data: {},
      statusText: 'Not Found',
      headers: {},
      config: {} as never,
    })
    vi.mocked(api.get).mockRejectedValueOnce(err)

    await expect(pricingApi.getPublicPricing()).resolves.toBeNull()
  })

  it('re-throws any other error (e.g. 500) instead of swallowing it', async () => {
    const err = new AxiosError('Server Error', '500', undefined, undefined, {
      status: 500,
      data: {},
      statusText: 'Internal Server Error',
      headers: {},
      config: {} as never,
    })
    vi.mocked(api.get).mockRejectedValueOnce(err)

    await expect(pricingApi.getPublicPricing()).rejects.toBe(err)
  })
})
