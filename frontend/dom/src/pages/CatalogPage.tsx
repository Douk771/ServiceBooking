import { ServiceLanding } from '@/components/landing/ServiceLanding'
import { StayCatalog } from '../components/catalog/StayCatalog'
import { StaySearchPanel } from '../components/catalog/StaySearchPanel'
import { stayLanding } from '../landing/stayLanding'

/**
 * `/` — catalog of houses of Sheregesh (US-37-05) on the shared landing template (ARCHITECTURE_CYCLE41.md §41.4.4). Filters live in
 * the URL (`useCatalogFilters`); the search panel is the header slot, the list is the catalog slot.
 */
export function CatalogPage() {
  return <ServiceLanding config={stayLanding} heroAside={<StaySearchPanel />} catalog={<StayCatalog />} />
}
