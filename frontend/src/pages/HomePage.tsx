import { ServiceLanding } from '../components/landing/ServiceLanding'
import { ZapisCompanyCatalog } from './home/ZapisCompanyCatalog'
import { zapisLanding } from './home/zapisLanding'

/** `/` «Записи» — единый шаблон главной с каталогом салонов (ARCHITECTURE_CYCLE37.md §37.4.2). */
export function HomePage() {
  return <ServiceLanding config={zapisLanding} catalog={<ZapisCompanyCatalog />} />
}
