import { ServiceLanding } from '@/components/landing/ServiceLanding'
import { ShopCatalog } from '../components/ShopCatalog'
import { goodsLanding } from '../landing/goodsLanding'

/** `/` (and the legacy `/city/:cityId`, which shows the same catalog) — единый шаблон главной с каталогом магазинов (ARCHITECTURE_CYCLE38.md §38.4.1). */
export function CatalogHomePage() {
  return <ServiceLanding config={goodsLanding} catalog={<ShopCatalog />} />
}
