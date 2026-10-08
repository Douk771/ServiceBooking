import type { PricingGridView, PricingLine } from '@/components/pricing/pricingLine'
import { formatIncludedLimit, formatIncludedLimitLine } from '@/utils/pricingFormat'
import { ordersPricingApi, type OrdersPublicPricingDto } from '../api/ordersPricing'

/** Строки лимитов карточки «Заказов» в порядке ARCHITECTURE_CYCLE38.md §38.7.1. */
export function ordersLimitLines(p: OrdersPublicPricingDto['plans'][number]): string[] {
  return [
    formatIncludedLimitLine(p.includedShops, 'магазины', 'магазина', 'магазинов'),
    formatIncludedLimitLine(p.includedMembers, 'участники', 'участника', 'участников'),
    // Поле никогда не null (потолок товаров) — «без ограничений» у товаров не бывает.
    `${formatIncludedLimit(p.includedProductsPerShop, 'товара', 'товаров')} в магазине`,
    p.includedOrdersPerMonth === null
      ? 'Заказы без ограничений'
      : `${formatIncludedLimit(p.includedOrdersPerMonth, 'заказа', 'заказов')} в месяц`,
  ]
}

export function toOrdersGridView(dto: OrdersPublicPricingDto): PricingGridView {
  return {
    plans: dto.plans.map((p) => ({
      id: p.id,
      name: p.name,
      description: p.description,
      pricePerMonth: p.pricePerMonth,
      highlights: p.highlights,
      sortOrder: p.sortOrder,
      isFree: p.isFree,
      isTrial: false,
      limitLines: ordersLimitLines(p),
    })),
    options: [],
    notice: dto.notice,
    legalNotice: dto.legalNotice ?? null,
  }
}

/** Линейка «Заказов»: GET /api/pricing/orders. */
export const ordersPricingLine: PricingLine = {
  queryKey: ['public-pricing', 'orders'],
  fetchGrid: () => ordersPricingApi.get().then((dto) => (dto ? toOrdersGridView(dto) : null)),
}
