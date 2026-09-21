import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { pricingApi } from '../../api/pricing'
import { Icon } from '../ui/Icon'
import { formatMonthlyPrice } from '../../utils/pricingFormat'

/**
 * Landing-page block, US-71: "companies that don't use the service yet should see prices before
 * logging in". Renders nothing while loading and nothing on 404 (publication switched off) or
 * network error — a broken/missing pricing block must never be the reason the homepage looks wrong
 * (ARCHITECTURE_CYCLE5.md §58, F5-2 "скрывается при 404").
 */
export function PricingTeaser() {
  const { data, isSuccess } = useQuery({
    queryKey: ['public-pricing'],
    queryFn: pricingApi.getPublicPricing,
    retry: false,
    staleTime: 60_000,
  })

  if (!isSuccess || !data) return null

  // `!isFree && pricePerMonth > 0` guards against a mis-configured plan (isFree=false, price=0)
  // rendering the nonsensical "от Бесплатно".
  const cheapestPaid = [...data.plans]
    .filter((p) => !p.isFree && p.pricePerMonth > 0)
    .sort((a, b) => a.pricePerMonth - b.pricePerMonth)[0]
  const highlightPlans = [...data.plans].sort((a, b) => a.sortOrder - b.sortOrder).slice(0, 3)

  return (
    <section className="bg-cream-deep py-[72px] px-8">
      <div className="max-w-[1180px] mx-auto">
        <div className="flex items-baseline justify-between mb-10 flex-wrap gap-3">
          <div>
            <h2 className="font-serif text-[32px] font-medium mb-2 text-ink">Тарифы</h2>
            {cheapestPaid && (
              <p className="text-[15px] text-ink-soft">
                Онлайн-запись и аналитика — от {formatMonthlyPrice(cheapestPaid.pricePerMonth)}
              </p>
            )}
          </div>
          <Link to="/pricing" className="inline-flex items-center gap-1.5 text-[15px] font-semibold text-ink border-b border-ink">
            Все тарифы
            <Icon name="arrow-right" size={15} strokeWidth={1.8} />
          </Link>
        </div>

        <div className="grid gap-6 md:grid-cols-3">
          {highlightPlans.map((plan) => (
            <div key={plan.id} className="bg-white border border-line rounded-[20px] p-6">
              <h3 className="font-serif text-[19px] font-medium text-ink mb-1">{plan.name}</h3>
              <p className="font-serif text-[24px] font-medium text-ink mb-3">{formatMonthlyPrice(plan.pricePerMonth)}</p>
              <ul className="flex flex-col gap-1.5">
                {plan.highlights.slice(0, 3).map((h, i) => (
                  <li key={`${i}-${h}`} className="flex items-start gap-1.5 text-[13px] text-ink-soft">
                    <Icon name="check" size={13} strokeWidth={2.2} className="shrink-0 mt-0.5 text-gold-dark" />
                    {h}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}
