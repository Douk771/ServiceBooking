/**
 * DTOs for GET /api/pricing and GET /api/admin/pricing/preview (API_CONTRACT_CYCLE5.md §39-40).
 *
 * Hand-written for now, not generated. ARCHITECTURE_CYCLE5.md §56.3 asks for these to come out of
 * `npm run types:api` against `contracts/openapi-cycle5.yaml` (openapi-typescript) — that devDependency
 * and script are not wired up yet. Flagged in the cycle report as an open item; once the script exists,
 * this file should be replaced by the generated one and callers re-checked against it.
 */

export interface PricingPlanDto {
  id: string
  name: string
  description: string
  pricePerMonth: number
  highlights: string[]
  /** null = без ограничения. */
  includedCompanies: number | null
  includedEmployees: number | null
  sortOrder: number
  isFree: boolean
}

export type PricingOptionKind = 'Quantity' | 'Toggle'

export interface PricingOptionDto {
  id: string
  name: string
  description?: string
  kind: PricingOptionKind
  pricePerMonth: number
  /** Nullable per contract — Toggle options have no per-unit name. */
  unitName: string | null
  /**
   * Собран сервером для Quantity-опций (API_CONTRACT_CYCLE5.md §39); для Kind === 'Toggle' сервер
   * всегда отдаёт null (PricingCatalogBuilder.BuildUnitPriceText) — рендерящий код обязан сам
   * подставлять formatMonthlyPrice(pricePerMonth) в этом случае, а не печатать null как есть.
   */
  unitPriceText: string | null
  sortOrder: number
}

export interface PublicPricingDto {
  version: string
  currency: string
  plans: PricingPlanDto[]
  options: PricingOptionDto[]
  notice: string
  /** Появится после юридической вычитки (API_CONTRACT_CYCLE5.md §39) — отсутствует/`null` до тех пор. */
  legalNotice?: string | null
}
