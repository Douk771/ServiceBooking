import { Link } from 'react-router-dom'
import { Icon } from '../ui/Icon'
import { usePricingGrid } from '../pricing/pricingLine'
import { formatMonthlyPrice } from '../../utils/pricingFormat'
import { H2, SECTION } from './classes'
import type { LandingPricingConfig } from './types'

/**
 * Секция 5: тарифы на главной (бывший PricingTeaser; ARCHITECTURE_CYCLE37.md §37.3.7). Пока грузится, при 404,
 * ошибке и пустом списке — ничего: сломанный блок цен не должен портить главную.
 */
export function LandingPricingSection({ config }: { config: LandingPricingConfig }) {
  const { data, isSuccess } = usePricingGrid(config.line)
  if (!isSuccess || !data || data.plans.length === 0) return null

  // `!isFree && pricePerMonth > 0` — страховка от тарифа с нулевой ценой («от Бесплатно»).
  const cheapestPaid = [...data.plans]
    .filter((p) => !p.isFree && p.pricePerMonth > 0)
    .sort((a, b) => a.pricePerMonth - b.pricePerMonth)[0]
  const cards = [...data.plans].sort((a, b) => a.sortOrder - b.sortOrder).slice(0, 3)

  return (
    <section aria-labelledby="pricing-title" className={SECTION}>
      <div className="flex items-baseline justify-between mb-10 flex-wrap gap-3">
        <div>
          <h2 id="pricing-title" className={`${H2} mb-2`}>
            Тарифы
          </h2>
          {cheapestPaid && (
            <p className="text-[15px] text-ink-soft">
              {config.lead} — от {formatMonthlyPrice(cheapestPaid.pricePerMonth)}
            </p>
          )}
        </div>
        <Link to="/pricing" className="inline-flex items-center gap-1.5 text-[15px] font-semibold text-ink border-b border-ink">
          Все тарифы
          <Icon name="arrow-right" size={15} strokeWidth={1.8} aria-hidden />
        </Link>
      </div>
      <div className="grid gap-6 md:grid-cols-3">
        {cards.map((plan) => (
          <div key={plan.id} className="bg-white border border-line rounded-[20px] p-6">
            <h3 className="font-serif text-[19px] font-medium text-ink mb-1">{plan.name}</h3>
            <p className="font-serif text-[24px] font-medium text-ink mb-3">{formatMonthlyPrice(plan.pricePerMonth)}</p>
            <ul className="flex flex-col gap-1.5">
              {[...plan.limitLines, ...plan.highlights.slice(0, 3)].map((h, i) => (
                <li key={`${i}-${h}`} className="flex items-start gap-1.5 text-[13px] text-ink-soft">
                  <Icon name="check" size={13} strokeWidth={2.2} className="shrink-0 mt-0.5 text-gold-dark" aria-hidden />
                  {h}
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </section>
  )
}
