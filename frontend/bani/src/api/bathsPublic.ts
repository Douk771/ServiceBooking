import { api } from '@/api/client'
import type { BathsCatalogCitiesDto, BathsCatalogPageDto, BathsPublicCompanyDto } from '../types'

export interface CatalogQuery {
  cityId?: number
  /** P1: only resources with a free start on this business day. */
  date?: string
  page?: number
  pageSize?: number
}

/** Anonymous routes of the catalog and the company page (API_CONTRACT_CYCLE42.md §42.22, §42.23). */
export const bathsPublicApi = {
  catalog: (params: CatalogQuery) => api.get<BathsCatalogPageDto>('/baths/catalog', { params }).then((r) => r.data),
  cities: () => api.get<BathsCatalogCitiesDto>('/baths/catalog/cities').then((r) => r.data),
  company: (slug: string) => api.get<BathsPublicCompanyDto>(`/baths/public/companies/${encodeURIComponent(slug)}`).then((r) => r.data),
}
