import { Link } from 'react-router-dom'
import { Icon } from '../ui/Icon'
import { formatMonthlyPrice } from '../../utils/pricingFormat'
import type { PricingPlanView } from './pricingLine'
import { MessengerAddonLines, type MessengerAddonDto, type MessengerAddonsNoteDto } from './MessengerAddonLines'

const TRIAL_BADGE = 'Пробный период'

/** Кнопка карточки тарифа: куда ведёт и что написано (решает PricingPageBody по тарифу и входу). */
export interface PlanCardAction {
  to: string
  label: string
  /** Доступное имя: начинается с видимого текста и называет тариф — кнопок с одинаковым текстом на странице несколько. */
  ariaLabel: string
}

interface Props {
  plan: PricingPlanView
  /** Кнопка внизу карточки; без неё карточка — только описание тарифа (как на главных). */
  action?: PlanCardAction
  /** Cycle 40 (Т40-L-11): серверные строки мессенджеров; показываются на всех карточках, опции докупаются отдельно от тарифа. */
  messengerAddons?: MessengerAddonDto[] | null
  messengerAddonsNote?: MessengerAddonsNoteDto | null
}

export function PlanCard({ plan, action, messengerAddons, messengerAddonsNote }: Props) {
  // Лейбл не повторяет заголовок: у тарифа «Пробный период» он дублировал бы название (поправка витрины 31.10.2026).
  const showTrialBadge = plan.isTrial && plan.name.trim().toLocaleLowerCase('ru') !== TRIAL_BADGE.toLocaleLowerCase('ru')
  return (
    <div
      className="flex flex-col min-w-0 rounded-[22px] p-7 xl:p-6 border bg-white text-ink border-line"
    >
      {/* Cycle 18 (API_CONTRACT_CYCLE18.md §370, US-18-01) — the badge is what keeps the trial from
          reading as "a second free plan" on the showcase. No duration/day-count is hardcoded here
          on purpose (§371: the frontend must not store the trial's duration in its own code) — the
          actual duration, uniqueness rule, mailing-window length and no-deletion promise are the
          admin-authored `highlights` bullets below (§370 operational rule for the plan-edit screen). */}
      {showTrialBadge && (
        <span className="self-start mb-3 text-xs font-semibold uppercase tracking-wide px-2.5 py-1 rounded-full bg-gold text-ink">
          {TRIAL_BADGE}
        </span>
      )}
      <h3 className="font-serif text-[22px] font-medium mb-1.5 break-words">{plan.name}</h3>
      <p className="text-sm leading-[1.55] mb-6 text-ink-soft">
        {plan.description}
      </p>

      <div className="mb-6">
        <span className="font-serif text-[32px] xl:text-[26px] font-medium whitespace-nowrap">{formatMonthlyPrice(plan.pricePerMonth)}</span>
      </div>

      <ul className="flex flex-col gap-2.5 mb-7">
        {[...plan.limitLines, ...plan.highlights].map((line, i) => (
          <li key={`${i}-${line}`} className="flex items-start gap-2 text-sm text-ink-soft">
            <Icon
              name="check"
              size={15}
              strokeWidth={2}
              className="shrink-0 mt-0.5 text-gold-dark"
            />
            {line}
          </li>
        ))}
      </ul>

      <MessengerAddonLines addons={messengerAddons} note={messengerAddonsNote} />

      {action && (
        // mt-auto: кнопки соседних карточек стоят на одной линии, сколько бы пунктов ни было у тарифа.
        <div className="mt-auto pt-6">
          <Link
            to={action.to}
            aria-label={action.ariaLabel}
            className="flex w-full items-center justify-center min-h-[44px] px-5 py-2.5 rounded-full text-sm font-semibold bg-ink hover:bg-ink/90 !text-cream transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-gold-dark"
          >
            {action.label}
          </Link>
        </div>
      )}
    </div>
  )
}
