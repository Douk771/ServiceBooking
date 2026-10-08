import { ServiceLanding } from '../components/landing/ServiceLanding'
import { ZapisCompanyCatalog } from './home/ZapisCompanyCatalog'
import { zapisLanding } from './home/zapisLanding'

/** `/` «Записи» — единый шаблон главной с каталогом салонов (ARCHITECTURE_CYCLE38.md §38.4.2). */
export function HomePage() {
  return <ServiceLanding config={zapisLanding} catalog={<ZapisCompanyCatalog />} />
}
