import { ServiceLanding } from '@/components/landing/ServiceLanding'
import { BathCatalog } from '../components/catalog/BathCatalog'
import { BathSearchPanel } from '../components/catalog/BathSearchPanel'
import { baniLanding } from '../landing/baniLanding'

/**
 * `/` — catalog of baths on the shared landing template (US-42-11): the search panel is the header slot, the list is the catalog
 * slot. Filters live in the URL (`useCatalogFilters`).
 */
export function CatalogPage() {
  return <ServiceLanding config={baniLanding} heroAside={<BathSearchPanel />} catalog={<BathCatalog />} />
}
