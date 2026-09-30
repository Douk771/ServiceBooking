import { api } from '@/api/client'
import type { CatalogListingDto } from '../types'

/** API_CONTRACT_CYCLE25.md §532. */
export const catalogListingApi = {
  get: (shopId: string) => api.get<CatalogListingDto>(`/shops/${shopId}/catalog-listing`).then((r) => r.data),
  put: (shopId: string, showInCatalog: boolean) =>
    api.put<CatalogListingDto>(`/shops/${shopId}/catalog-listing`, { showInCatalog }).then((r) => r.data),
}
