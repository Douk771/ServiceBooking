import { formatRub } from '@/utils/money'
import type { ServiceQuoteDto } from '../../types'
import { Skeleton } from '../StatePanels'

/**
 * The sum of a session as the server counted it (`ServiceQuoteDto`): every line with its own label, the total, the prepayment and
 * what is paid on the spot. The arithmetic and every text are the server's (ЮР39-1: no refund amount is ever composed here). A
 * quote that is not `ok` shows its problems and no amounts.
 */
export function ServiceQuoteSummary({ quote, loading, stale }: { quote: ServiceQuoteDto | undefined; loading?: boolean; stale?: boolean }) {
  if (loading && !quote) return <Skeleton className="h-40" />
  if (!quote) return null
  if (!quote.ok) {
    return (
      <ul role="alert" className="flex flex-col gap-1 rounded-2xl bg-danger-bg px-4 py-3 text-sm text-danger" data-testid="service-quote-problems">
        {quote.problems.map((p) => (
          <li key={p.code}>{p.message}</li>
        ))}
      </ul>
    )
  }
  return (
    <div className={stale ? 'opacity-60 transition-opacity' : ''} aria-live="polite" data-testid="service-quote">
      {quote.time && <p className="mb-2 text-sm font-semibold text-ink">{quote.time.label}</p>}
      <dl className="text-sm">
        {quote.lines.map((l, i) => (
          <div key={`${l.kind}-${i}`} className="flex items-baseline justify-between gap-4 border-b border-line/70 py-2">
            <dt className="text-ink-soft">{l.label}</dt>
            <dd className="shrink-0 font-medium tabular-nums text-ink">{l.amountRub === 0 ? 'бесплатно' : formatRub(l.amountRub)}</dd>
          </div>
        ))}
        <div className="flex items-baseline justify-between gap-4 py-2.5">
          <dt className="font-semibold text-ink">Итого</dt>
          <dd className="text-lg font-semibold tabular-nums text-ink">{formatRub(quote.totalRub)}</dd>
        </div>
        {quote.prepayRub > 0 ? (
          <>
            <div className="flex items-baseline justify-between gap-4 rounded-xl bg-cream-deep px-3 py-2">
              <dt className="text-ink">Предоплата{quote.prepayPercent ? ` ${quote.prepayPercent} %` : ''}</dt>
              <dd className="font-semibold tabular-nums text-ink">{formatRub(quote.prepayRub)}</dd>
            </div>
            <div className="flex items-baseline justify-between gap-4 px-3 py-2">
              <dt className="text-ink-soft">На месте</dt>
              <dd className="tabular-nums text-ink">{formatRub(quote.dueOnSiteRub)}</dd>
            </div>
          </>
        ) : (
          quote.payOnSiteText && <p className="rounded-xl bg-cream-deep px-3 py-2 text-ink">{quote.payOnSiteText}</p>
        )}
      </dl>
      {quote.prepayRub > 0 && quote.holdMinutes != null && (
        <p className="mt-3 text-xs text-ink-soft">
          Время удерживается {quote.holdMinutes} минут — за это время внесите предоплату по реквизитам и приложите подтверждение оплаты.
          Реквизиты появятся на странице заказа.
        </p>
      )}
      {quote.cancellationSummary && <p className="mt-2 text-xs text-ink-soft">Отмена: {quote.cancellationSummary}</p>}
    </div>
  )
}
