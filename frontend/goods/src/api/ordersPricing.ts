import { AxiosError } from 'axios'
import { api } from '@/api/client'
import type { components } from '@/types/api-cycle38.generated'

export type OrdersPublicPricingDto = components['schemas']['OrdersPublicPricingDto']
export type OrdersPublicPlanDto = components['schemas']['OrdersPublicPlanDto']

/**
 * GET /api/pricing/orders — anonymous (API_CONTRACT_CYCLE38.md §38.22). 404 = в линейке «Заказы» нет активного публичного
 * тарифа: ожидаемое состояние (на бою до `ops tariffs apply`), поэтому резолвится в `null`, а не бросает.
 */
export const ordersPricingApi = {
  get: (): Promise<OrdersPublicPricingDto | null> =>
    api
      .get<OrdersPublicPricingDto>('/pricing/orders')
      .then((r) => r.data)
      .catch((err) => {
        if (err instanceof AxiosError && err.response?.status === 404) return null
        throw err
      }),
}
