import type { PhotoRetention, OptionAvailability, PlanOptionRuleDto } from '../../api/plans'

// Вынесено из PlansTab.tsx: react-refresh/only-export-components — файл, экспортирующий и компонент,
// и обычные функции, лишается горячей подмены целиком, и правка разметки перезагружает страницу
// вместе с состоянием формы. Здесь живёт форма плана: её тип, её потолки и маппинг в контракт и
// обратно — всё, что можно проверить без рендера (PlansTab.test.ts импортирует уже отсюда).

// AdminPlanDto.highlights allows up to 10 entries on write (contract + PricingCatalogBuilder.MaxHighlights).
// The PUBLIC pricing page only ever shows the first 5 (PricingPlanDto.highlights maxItems, and
// PricingCatalogBuilder.PublicMaxHighlights) — that's a display cap, not a write cap, so the editor
// must not block entering a 6th..10th bullet; it just needs to say plainly which ones are shown.
export const MAX_HIGHLIGHTS = 10
export const PUBLIC_MAX_HIGHLIGHTS = 5
export const MAX_HIGHLIGHT_LENGTH = 120

export interface PlanForm {
  name: string
  pricePerMonth: string
  maxEmployees: string
  maxCompanies: string
  allowOnlineBooking: boolean
  allowMailing: boolean
  allowAnalytics: boolean
  allowPublicListing: boolean
  allowOnlinePayment: boolean
  description: string
  notifyDaysBefore: string
  photoQuotaMb: string
  photoRetention: PhotoRetention
  highlights: string[]
  /** optionId -> rule. An option absent here is Unavailable, matching the contract's "missing means
   *  Unavailable" rule for AdminPlanDto.options. */
  optionRules: Record<string, { availability: OptionAvailability; includedQuantity: string }>
}

export const defaultForm: PlanForm = {
  name: '',
  pricePerMonth: '0',
  maxEmployees: '',
  maxCompanies: '',
  allowOnlineBooking: true,
  allowMailing: false,
  allowAnalytics: false,
  allowPublicListing: true,
  allowOnlinePayment: false,
  description: '',
  notifyDaysBefore: '7',
  photoQuotaMb: '1024',
  photoRetention: 'TwelveMonths',
  highlights: [],
  optionRules: {},
}

export function optionRulesToForm(rules: PlanOptionRuleDto[]): PlanForm['optionRules'] {
  const map: PlanForm['optionRules'] = {}
  for (const r of rules) {
    map[r.optionId] = {
      availability: r.availability,
      includedQuantity: r.includedQuantity != null ? String(r.includedQuantity) : '',
    }
  }
  return map
}

export function optionRulesToPayload(rules: PlanForm['optionRules']): PlanOptionRuleDto[] {
  return Object.entries(rules)
    .filter(([, rule]) => rule.availability !== 'Unavailable')
    .map(([optionId, rule]) => ({
      optionId,
      availability: rule.availability,
      includedQuantity:
        rule.availability === 'Included' && rule.includedQuantity.trim() !== ''
          ? parseInt(rule.includedQuantity)
          : null,
    }))
}
