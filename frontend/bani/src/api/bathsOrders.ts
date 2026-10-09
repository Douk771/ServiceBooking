import { api } from '@/api/client'
import type { components } from '@/types/api-cycle42.generated'

export type MyBathOrdersDto = components['schemas']['MyBathOrdersDto']
export type MyBathOrderDto = components['schemas']['MyBathOrderDto']

/** `GET /api/baths/service-orders/my` (API_CONTRACT_CYCLE42.md §42.26) — «Мои брони». The rest of the guest routes is `bathsVertical.api`. */
export const bathsOrdersApi = {
  my: () => api.get<MyBathOrdersDto>('/baths/service-orders/my').then((r) => r.data),
}
