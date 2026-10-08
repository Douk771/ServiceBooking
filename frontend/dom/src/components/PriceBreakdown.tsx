import { formatRub } from '@/utils/money'
import type { StayChargeLineDto } from '../types'

interface Props {
  lines: StayChargeLineDto[]
  totalRub: number
  prepayPercent: number
  prepayRub: number
  dueAtCheckInRub: number
  className?: string
}

/**
 * The sum of a stay as the server built it (rows of the quote / booking snapshot): every line with its own label, the total, the
 * prepayment and what is paid at check-in. The labels are the server's; the arithmetic is the server's too — nothing here adds up.
 */
export function PriceBreakdown({ lines, totalRub, prepayPercent, prepayRub, dueAtCheckInRub, className = '' }: Props) {
  return (
    <dl className={`text-sm ${className}`}>
      {lines.map((l, i) => (
        <div key={`${l.kind}-${i}`} className="flex items-baseline justify-between gap-4 border-b border-line/70 py-2">
          <dt className="text-ink-soft">{l.label}</dt>
          <dd className="shrink-0 font-medium tabular-nums text-ink">{l.amountRub === 0 ? 'бесплатно' : formatRub(l.amountRub)}</dd>
        </div>
      ))}
      <div className="flex items-baseline justify-between gap-4 py-2.5">
        <dt className="font-semibold text-ink">Итого</dt>
        <dd className="text-lg font-semibold tabular-nums text-ink">{formatRub(totalRub)}</dd>
      </div>
      {prepayRub > 0 ? (
        <>
          <div className="flex items-baseline justify-between gap-4 rounded-xl bg-cream-deep px-3 py-2">
            <dt className="text-ink">Предоплата {prepayPercent} %</dt>
            <dd className="font-semibold tabular-nums text-ink">{formatRub(prepayRub)}</dd>
          </div>
          <div className="flex items-baseline justify-between gap-4 px-3 py-2">
            <dt className="text-ink-soft">К оплате при заселении</dt>
            <dd className="tabular-nums text-ink">{formatRub(dueAtCheckInRub)}</dd>
          </div>
        </>
      ) : (
        <div className="flex items-baseline justify-between gap-4 rounded-xl bg-cream-deep px-3 py-2">
          <dt className="text-ink">Предоплата не нужна</dt>
          <dd className="tabular-nums text-ink">к оплате при заселении {formatRub(dueAtCheckInRub)}</dd>
        </div>
      )}
    </dl>
  )
}
