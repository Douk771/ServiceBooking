import type { QueryClient } from '@tanstack/react-query'

/** Cycle 49 (A49-5): services and providers decide whether the salon is in the catalog, so the «Каталог ezbook.ru» block must re-read. */
export const invalidateSalonCatalogListing = (qc: QueryClient, companyId: string) =>
  qc.invalidateQueries({ queryKey: ['company-catalog-listing', companyId] })
