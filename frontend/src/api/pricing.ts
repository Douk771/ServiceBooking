import { AxiosError } from 'axios'
import { api } from './client'
import type { PublicPricingDto } from '../types/pricing'

/**
 * GET /api/pricing — public, anonymous (API_CONTRACT_CYCLE5.md §39). 404 means the platform switch
 * `pricing.public-enabled` is off and the caller must render nothing (no page, no teaser, no nav
 * link) — that is a valid, expected state, not an error, so it resolves to `null` instead of throwing.
 */
export const pricingApi = {
  getPublicPricing: (): Promise<PublicPricingDto | null> =>
    api
      .get<PublicPricingDto>('/pricing')
      .then((r) => r.data)
      .catch((err) => {
        if (err instanceof AxiosError && err.response?.status === 404) return null
        throw err
      }),
}
