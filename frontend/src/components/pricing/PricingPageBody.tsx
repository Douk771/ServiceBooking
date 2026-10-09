import { useEffect } from 'react'
import { Link } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { Card } from '../ui/Card'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import type { AuthRouteAction } from '../landing/types'
import { PlanCard } from './PlanCard'
import { OptionRow } from './OptionRow'
import { usePricingGrid, type PricingLine } from './pricingLine'

// Cycle 28 (FE-4, ARCHITECTURE_CYCLE28.md §582.3): the catalog is five plans since «Записи» got its grid (free, trial,
// Студия, Салон, Сеть). Two columns from `md`, three from `lg`, all five in a row from `xl`, where the container is
// widened so a card keeps ~195px of text width instead of being squeezed by the old 1180px cap.
const PRICING_CONTAINER = 'max-w-[1180px] xl:max-w-[1360px] mx-auto px-8 pt-16 pb-24'
const PRICING_GRID_BASE = 'grid gap-5 md:grid-cols-2 lg:grid-cols-3'
// Литеральная карта (Tailwind не видит собранные строки): колонки на xl по числу тарифов.
const XL_COLS: Record<number, string> = { 4: 'xl:grid-cols-4', 5: 'xl:grid-cols-5' }
const gridClass = (count: number) => `${PRICING_GRID_BASE} ${XL_COLS[count] ?? 'xl:grid-cols-5'}`

/**
 * Тело публичного маршрута `/pricing` для любой линейки (ARCHITECTURE_CYCLE38.md §38.7.2). Public route `/pricing` (ARCHITECTURE_CYCLE7.md §56.1) — reachable without auth, and outside the
 * consent gate (added to CONSENT_GATE_BYPASS_PATHS in App.tsx) so a logged-in user with a pending
 * legal update can still see prices. US-71.
 *
 * 404 from the API (line.fetchGrid → null) means there is no grid to show: for «Запись» the publication switch
 * (`pricing.public-enabled`) is off, for «Заказы» the line has no active public tariff (ARCHITECTURE_CYCLE38.md §38.9.2).
 * That is an expected state (API_CONTRACT_CYCLE7.md §39), not an error: the page renders a plain "not available
 * yet" notice instead of an error screen.
 */
interface Props {
  line: PricingLine
  documentTitle: string
  cta: AuthRouteAction
}

export function PricingPageBody({ line, documentTitle, cta }: Props) {
  const { data, isLoading, isError, error, refetch, isRefetching } = usePricingGrid(line)
  const authed = useAuthStore((s) => s.isAuthenticated())

  useEffect(() => {
    document.title = documentTitle
  }, [documentTitle])

  if (isLoading) {
    return (
      <div className={PRICING_CONTAINER}>
        <div className="h-9 w-1/3 bg-cream-deep rounded-xl animate-pulse mb-10" />
        <div className={gridClass(5)}>
          {Array.from({ length: 5 }).map((_, i) => (
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

  // 404 → no grid for this line (switch off / no public tariff). Not an error (API_CONTRACT_CYCLE7.md §39).
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

  return (
    <div className={PRICING_CONTAINER}>
      <header className="max-w-[620px] mb-12">
        <h1 className="font-serif text-[36px] md:text-[44px] font-medium text-ink mb-3">Тарифы и цены</h1>
        <p className="text-[15px] leading-[1.6] text-ink-soft">{data.notice}</p>
        {data.legalNotice && <p className="text-xs leading-[1.6] text-muted mt-3">{data.legalNotice}</p>}
      </header>

      <div className={`${gridClass(sortedPlans.length)} mb-16`}>
        {sortedPlans.map((plan) => (
          <PlanCard key={plan.id} plan={plan} messengerAddons={data.messengerAddons} messengerAddonsNote={data.messengerAddonsNote} />
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
        <Link to={authed ? cta.authedTo : cta.guestTo} className="text-[15px] font-semibold text-ink border-b border-ink">
          {cta.label}
        </Link>
      </div>
    </div>
  )
}
