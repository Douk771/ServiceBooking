import { api } from './client'
import type { SalonCatalogListingDto } from '../types'

/** API_CONTRACT_CYCLE31.md §31.21–§31.22 — the salon's "Каталог ezbook.ru" block. Owner of the company or SuperAdmin. */
export const companyCatalogListingApi = {
  get: (companyId: string) =>
    api.get<SalonCatalogListingDto>(`/companies/${companyId}/catalog-listing`).then((r) => r.data),
  put: (companyId: string, showInCatalog: boolean) =>
    api.put<SalonCatalogListingDto>(`/companies/${companyId}/catalog-listing`, { showInCatalog }).then((r) => r.data),
}
