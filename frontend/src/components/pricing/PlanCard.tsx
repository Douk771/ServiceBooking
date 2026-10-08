import { Icon } from '../ui/Icon'
import { formatIncludedLimitLine, formatMonthlyPrice } from '../../utils/pricingFormat'
import type { PricingPlanDto } from '../../types/pricing'
import { MessengerAddonLines, type MessengerAddonDto, type MessengerAddonsNoteDto } from './MessengerAddonLines'

interface Props {
  plan: PricingPlanDto
  /** The first paid plan (right after the free one, in sortOrder) gets a visual accent — a cheap,
   *  honest way to guide the eye without a comparison table (ARCHITECTURE_CYCLE7.md §56.2 rules out
   *  a table outright). Not "recommended": the contract has no such flag. */
  featured?: boolean
  /** Cycle 40 (Т40-L-11): серверные строки мессенджеров; только платные тарифы, как их передаёт страница. */
  messengerAddons?: MessengerAddonDto[] | null
  messengerAddonsNote?: MessengerAddonsNoteDto | null
}

export function PlanCard({ plan, featured = false, messengerAddons, messengerAddonsNote }: Props) {
  return (
    <div
      className={`flex flex-col min-w-0 rounded-[22px] p-7 xl:p-6 border transition-shadow ${
        featured ? 'bg-ink text-cream border-ink shadow-card' : 'bg-white text-ink border-line'
      }`}
    >
      {/* Cycle 18 (API_CONTRACT_CYCLE18.md §370, US-18-01) — the badge is what keeps the trial from
          reading as "a second free plan" on the showcase. No duration/day-count is hardcoded here
          on purpose (§371: the frontend must not store the trial's duration in its own code) — the
          actual duration, uniqueness rule, mailing-window length and no-deletion promise are the
          admin-authored `highlights` bullets below (§370 operational rule for the plan-edit screen). */}
      {plan.isTrial && (
        <span className="self-start mb-3 text-xs font-semibold uppercase tracking-wide px-2.5 py-1 rounded-full bg-gold text-ink">
          Пробный период
        </span>
      )}
      <h3 className="font-serif text-[22px] font-medium mb-1.5 break-words">{plan.name}</h3>
      <p className={`text-sm leading-[1.55] mb-6 ${featured ? 'text-cream/75' : 'text-ink-soft'}`}>
        {plan.description}
      </p>

      <div className="mb-6">
        <span className="font-serif text-[32px] xl:text-[26px] font-medium whitespace-nowrap">{formatMonthlyPrice(plan.pricePerMonth)}</span>
      </div>

      <ul className="flex flex-col gap-2.5 mb-7">
        <li className={`flex items-start gap-2 text-sm ${featured ? 'text-cream/90' : 'text-ink-soft'}`}>
          <Icon name="check" size={15} strokeWidth={2} className={`shrink-0 mt-0.5 ${featured ? 'text-gold' : 'text-gold-dark'}`} />
          {formatIncludedLimitLine(plan.includedCompanies, 'компании', 'компании', 'компаний')}
        </li>
        <li className={`flex items-start gap-2 text-sm ${featured ? 'text-cream/90' : 'text-ink-soft'}`}>
          <Icon name="check" size={15} strokeWidth={2} className={`shrink-0 mt-0.5 ${featured ? 'text-gold' : 'text-gold-dark'}`} />
          {formatIncludedLimitLine(plan.includedEmployees, 'сотрудники', 'сотрудника', 'сотрудников')}
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
      <MessengerAddonLines addons={messengerAddons} note={messengerAddonsNote} inverted={featured} />
    </div>
  )
}
