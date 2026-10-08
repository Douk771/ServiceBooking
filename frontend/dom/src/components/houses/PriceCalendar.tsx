import { useState } from 'react'
import { Icon } from '@/components/ui/Icon'
import type { DateRangeDto, HousePriceMode } from '../../types'
import { buildPriceMonth } from '../../utils/priceCalendar'
import { WEEKDAY_HEADERS, addMonths, firstOfMonth, formatDateWithWeekday, formatMonthTitle } from '../../utils/stayDates'
import { priceShort } from '../../utils/staySelection'
import type { PricePeriod } from '../../utils/stayPricing'

/**
 * Price per night by date, as the guest would be charged (US-37-13). Dates with no price in the future are marked and named in words —
 * a night without a price cannot be booked. A click on a date hands it to the period form.
 */
export function PriceCalendar({
  today,
  house,
  periods,
  uncovered,
  onPick,
}: {
  today: string
  house: { mode: HousePriceMode; constantPriceRub?: number | null }
  periods: readonly PricePeriod[]
  uncovered: readonly DateRangeDto[]
  onPick?: (date: string) => void
}) {
  const [month, setMonth] = useState(() => firstOfMonth(today))
  const cells = buildPriceMonth(month, house, periods, uncovered, today)

  return (
    <div data-testid="price-calendar">
      <div className="mb-2 flex items-center justify-between">
        <button
          type="button"
          aria-label="Предыдущий месяц"
          disabled={month <= firstOfMonth(today)}
          onClick={() => setMonth(addMonths(month, -1))}
          className="flex h-11 w-11 items-center justify-center rounded-full border border-line text-ink-soft disabled:opacity-35"
        >
          <Icon name="chevron-left" size={16} strokeWidth={1.8} />
        </button>
        <p className="text-sm font-semibold text-ink" aria-live="polite">
          {formatMonthTitle(month)}
        </p>
        <button
          type="button"
          aria-label="Следующий месяц"
          onClick={() => setMonth(addMonths(month, 1))}
          className="flex h-11 w-11 items-center justify-center rounded-full border border-line text-ink-soft"
        >
          <Icon name="chevron-right" size={16} strokeWidth={1.8} />
        </button>
      </div>
      <div className="grid grid-cols-7 text-center text-[11px] font-medium uppercase tracking-wide text-muted">
        {WEEKDAY_HEADERS.map((w) => (
          <span key={w} className="py-1">
            {w}
          </span>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-px">
        {cells.map((c, i) =>
          c === null ? (
            <span key={`e${i}`} />
          ) : (
            <button
              key={c.date}
              type="button"
              disabled={!onPick || c.past}
              onClick={() => onPick?.(c.date)}
              aria-label={`${formatDateWithWeekday(c.date)}: ${c.priceRub != null ? `${c.priceRub.toLocaleString('ru-RU')} ₽ за ночь` : c.uncovered ? 'нет цены, ночь нельзя забронировать' : 'цены нет'}`}
              className={`flex min-h-[44px] flex-col items-center justify-center rounded-lg border text-sm leading-none ${
                c.past
                  ? 'border-transparent text-line-strong'
                  : c.uncovered
                    ? 'border-dashed border-warning bg-warning-bg text-warning'
                    : c.priceRub != null
                      ? 'border-transparent bg-white text-ink hover:border-gold'
                      : 'border-transparent text-muted'
              }`}
            >
              <span className="font-medium">{Number(c.date.slice(8))}</span>
              <span className="mt-0.5 text-[9px]">{c.priceRub != null ? priceShort(c.priceRub) : c.uncovered ? 'нет цены' : ''}</span>
            </button>
          ),
        )}
      </div>
      <p className="mt-2 text-[11px] text-muted">Цена ночи — по дате, с которой ночь начинается.</p>
    </div>
  )
}
