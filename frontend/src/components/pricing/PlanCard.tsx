import { Icon } from '../ui/Icon'
import { formatIncludedLimit, formatMonthlyPrice } from '../../utils/pricingFormat'
import type { PricingPlanDto } from '../../types/pricing'

interface Props {
  plan: PricingPlanDto
  /** The first paid plan (right after the free one, in sortOrder) gets a visual accent — a cheap,
   *  honest way to guide the eye without a comparison table (ARCHITECTURE_CYCLE7.md §56.2 rules out
   *  a table outright). Not "recommended": the contract has no such flag. */
  featured?: boolean
}

export function PlanCard({ plan, featured = false }: Props) {
  return (
    <div
      className={`flex flex-col rounded-[22px] p-8 border transition-shadow ${
        featured ? 'bg-ink text-cream border-ink shadow-card' : 'bg-white text-ink border-line'
      }`}
    >
      <h3 className="font-serif text-[22px] font-medium mb-1.5">{plan.name}</h3>
      <p className={`text-sm leading-[1.55] mb-6 ${featured ? 'text-cream/75' : 'text-ink-soft'}`}>
        {plan.description}
      </p>

      <div className="mb-6">
        <span className="font-serif text-[34px] font-medium">{formatMonthlyPrice(plan.pricePerMonth)}</span>
      </div>

      <ul className="flex flex-col gap-2.5 mb-7">
        <li className={`flex items-start gap-2 text-sm ${featured ? 'text-cream/90' : 'text-ink-soft'}`}>
          <Icon name="check" size={15} strokeWidth={2} className={`shrink-0 mt-0.5 ${featured ? 'text-gold' : 'text-gold-dark'}`} />
          {formatIncludedLimit(plan.includedCompanies, 'компании', 'компаний')}
        </li>
        <li className={`flex items-start gap-2 text-sm ${featured ? 'text-cream/90' : 'text-ink-soft'}`}>
          <Icon name="check" size={15} strokeWidth={2} className={`shrink-0 mt-0.5 ${featured ? 'text-gold' : 'text-gold-dark'}`} />
          {formatIncludedLimit(plan.includedEmployees, 'сотрудника', 'сотрудников')}
        </li>
        {plan.highlights.map((h, i) => (
          <li key={`${i}-${h}`} className={`flex items-start gap-2 text-sm ${featured ? 'text-cream/90' : 'text-ink-soft'}`}>
            <Icon
              name="check"
              size={15}
              strokeWidth={2}
              className={`shrink-0 mt-0.5 ${featured ? 'text-gold' : 'text-gold-dark'}`}
            />
            {h}
          </li>
        ))}
      </ul>
    </div>
  )
}
