import { api } from '@/api/client'
import type { GoodsCatalogPageDto } from '../types'

/** API_CONTRACT_CYCLE25.md §531 — anonymous. */
export const goodsCatalogApi = {
  list: (params: { cityId?: number; openNow?: boolean; search?: string; page?: number }) =>
    api.get<GoodsCatalogPageDto>('/goods/catalog', { params }).then((r) => r.data),
}
