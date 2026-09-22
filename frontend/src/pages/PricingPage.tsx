import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { pricingApi } from '../api/pricing'
import { PlanCard } from '../components/pricing/PlanCard'
import { OptionRow } from '../components/pricing/OptionRow'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'

/**
 * Public route `/pricing` (ARCHITECTURE_CYCLE7.md §56.1) — reachable without auth, and outside the
 * consent gate (added to CONSENT_GATE_BYPASS_PATHS in App.tsx) so a logged-in user with a pending
 * legal update can still see prices. US-71.
 *
 * 404 from the API means the publication switch (`pricing.public-enabled`) is off — that is an
 * expected state (API_CONTRACT_CYCLE7.md §39), not an error: the page renders a plain "not available
 * yet" notice instead of an error screen.
 */
export function PricingPage() {
  const { data, isLoading, isError, error, refetch, isRefetching } = useQuery({
    queryKey: ['public-pricing'],
    queryFn: pricingApi.getPublicPricing,
    retry: false,
    staleTime: 20_000,
  })

  useEffect(() => {
    document.title = 'Тарифы и цены — ServiceBooking'
  }, [])

  if (isLoading) {
    return (
      <div className="max-w-[1180px] mx-auto px-8 pt-16 pb-24">
        <div className="h-9 w-1/3 bg-cream-deep rounded-xl animate-pulse mb-10" />
        <div className="grid gap-6 md:grid-cols-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-72 bg-cream-deep rounded-[22px] animate-pulse" />
          ))}
        </div>
      </div>
    )
  }

  if (isError) {
    return (
      <div className="max-w-[760px] mx-auto px-8 pt-16 pb-24">
        <Card className="p-12 text-center text-muted">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg font-medium text-ink-soft mb-4">Не удалось загрузить тарифы. Попробуйте снова.</p>
          <Button variant="secondary" loading={isRefetching} onClick={() => refetch()}>
            Попробовать снова
          </Button>
        </Card>
        {import.meta.env.DEV && (error as Error)?.message}
      </div>
    )
  }

  // 404 → publication is switched off. Not an error (API_CONTRACT_CYCLE7.md §39).
  if (!data) {
    return (
      <div className="max-w-[760px] mx-auto px-8 pt-16 pb-24">
        <Card className="p-12 text-center text-muted">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg text-ink-soft">Тарифы пока не опубликованы.</p>
        </Card>
      </div>
    )
  }

  const sortedPlans = [...data.plans].sort((a, b) => a.sortOrder - b.sortOrder || a.pricePerMonth - b.pricePerMonth)
  const sortedOptions = [...data.options].sort((a, b) => a.sortOrder - b.sortOrder || a.pricePerMonth - b.pricePerMonth)
  // The paid plan right after the free one gets the visual accent — a stand-in for "recommended"
  // without inventing a field the contract doesn't have.
  const featuredPlanId = sortedPlans.find((p) => !p.isFree)?.id

  return (
    <div className="max-w-[1180px] mx-auto px-8 pt-16 pb-24">
      <header className="max-w-[620px] mb-12">
        <h1 className="font-serif text-[36px] md:text-[44px] font-medium text-ink mb-3">Тарифы и цены</h1>
        <p className="text-[15px] leading-[1.6] text-ink-soft">{data.notice}</p>
        {data.legalNotice && <p className="text-xs leading-[1.6] text-muted mt-3">{data.legalNotice}</p>}
      </header>

      <div className="grid gap-6 md:grid-cols-3 mb-16">
        {sortedPlans.map((plan) => (
          <PlanCard key={plan.id} plan={plan} featured={plan.id === featuredPlanId} />
        ))}
      </div>

      {sortedOptions.length > 0 && (
        <section className="max-w-[720px]">
          <h2 className="font-serif text-[24px] font-medium text-ink mb-1">Дополнительные опции</h2>
          {/* No blanket "available on any paid plan" claim here: per-plan availability is governed by
              PlanOptionRule server-side (ARCHITECTURE_CYCLE7.md §43.3, fail-closed), and the public
              contract doesn't expose which plans a given option is available on. */}
          <p className="text-sm text-ink-soft mb-5">Наличие опций зависит от выбранного тарифа.</p>
          <Card className="p-6">
            {sortedOptions.map((option) => (
              <OptionRow key={option.id} option={option} />
            ))}
          </Card>
        </section>
      )}

      <div className="mt-14 text-center">
        <Link to="/register" className="text-[15px] font-semibold text-ink border-b border-ink">
          Зарегистрировать компанию
        </Link>
      </div>
    </div>
  )
}
