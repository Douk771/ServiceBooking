import { pricingApi } from '../../api/pricing'
import { formatIncludedLimitLine } from '../../utils/pricingFormat'
import type { PublicPricingDto } from '../../types/pricing'
import type { PricingGridView, PricingLine } from './pricingLine'

export function toZapisGridView(dto: PublicPricingDto): PricingGridView {
  return {
    plans: dto.plans.map((p) => ({
      id: p.id,
      name: p.name,
      description: p.description,
      pricePerMonth: p.pricePerMonth,
      highlights: p.highlights,
      sortOrder: p.sortOrder,
      isFree: p.isFree,
      isTrial: p.isTrial ?? false,
      limitLines: [
        formatIncludedLimitLine(p.includedCompanies, 'компании', 'компании', 'компаний'),
        formatIncludedLimitLine(p.includedEmployees, 'сотрудники', 'сотрудника', 'сотрудников'),
      ],
    })),
    options: dto.options,
    notice: dto.notice,
    legalNotice: dto.legalNotice ?? null,
  }
}

/** Линейка «Записи»: GET /api/pricing (ARCHITECTURE_CYCLE38.md §38.7.1). */
export const zapisPricingLine: PricingLine = {
  queryKey: ['public-pricing', 'services'],
  fetchGrid: () => pricingApi.getPublicPricing().then((dto) => (dto ? toZapisGridView(dto) : null)),
}
