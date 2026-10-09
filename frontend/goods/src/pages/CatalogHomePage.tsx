import { ServiceLanding } from '@/components/landing/ServiceLanding'
import { ShopCatalog } from '../components/ShopCatalog'
import { goodsLanding } from '../landing/goodsLanding'

/** `/` and `/city/:cityId` — единый шаблон главной с каталогом магазинов (ARCHITECTURE_CYCLE38.md §38.4.1). */
export function CatalogHomePage() {
  return <ServiceLanding config={goodsLanding} catalog={<ShopCatalog />} />
}
