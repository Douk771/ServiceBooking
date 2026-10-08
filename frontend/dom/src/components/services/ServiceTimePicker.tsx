import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { formatRub } from '@/utils/money'
import type { ServiceAvailabilityDto, ServiceItemPublicDto, ServiceStartsDto, StartDto } from '../../types'
import { addDays } from '../../utils/stayDates'
import { getStayErrorMessage } from '../../utils/stayError'
import { MINUTES_PER_DAY } from '../../utils/businessClock'
import { keepHours, clampQuantity, type SessionPick } from '../../utils/serviceSelection'
import { ErrorState, Skeleton } from '../StatePanels'
import { Stepper } from '../Stepper'

const PAGE_DAYS = 14

interface Props {
  /** Scopes the query keys: one picker per service and mode. */
  scope: string
  /** Business date to open on (the `?date=` of the page). */
  initialDate?: string | null
  items: readonly ServiceItemPublicDto[]
  loadAvailability: (from: string | undefined, days: number) => Promise<ServiceAvailabilityDto>
  loadStarts: (date: string) => Promise<ServiceStartsDto>
  onChange: (pick: SessionPick | null) => void
  /** Bumped by the owner of the picker after a refusal «время недоступно»: the lists are re-read and the choice starts over. */
  resetSignal?: number
}

/**
 * Four steps (ARCHITECTURE_CYCLE39.md §39.14.2): date → start → hours → positions. The dates are captioned with calendar dates
 * («пт 15 янв»); a start after midnight goes to its own group at the end, tagged «(ночь на сб)»; the end of a session is the
 * server's `endLabel` (guest wording, two calendar dates across midnight). Every target is ≥ 44×44, every status is text, not colour.
 * Positions start at 0: nothing is added for the guest (ЮР39-6).
 */
export function ServiceTimePicker({ scope, initialDate, items, loadAvailability, loadStarts, onChange, resetSignal = 0 }: Props) {
  const [offset, setOffset] = useState(0)
  const [date, setDate] = useState<string | null>(initialDate ?? null)
  const [startMinute, setStartMinute] = useState<number | null>(null)
  const [hours, setHours] = useState<number | null>(null)
  const [quantities, setQuantities] = useState<Record<string, number>>({})

  // The first page starts at the server's current business day; later pages are counted from that `today`.
  const todayRef = useRef<string | null>(null)
  const daysQuery = useQuery({
    queryKey: ['stays-service-availability', scope, offset, resetSignal],
    queryFn: async () => {
      const from = offset === 0 || !todayRef.current ? undefined : addDays(todayRef.current, offset * PAGE_DAYS)
      const a = await loadAvailability(from, PAGE_DAYS)
      if (offset === 0) todayRef.current = a.today
      return a
    },
    staleTime: 0,
  })
  const days = daysQuery.data?.days

  const starts = useQuery({
    queryKey: ['stays-service-starts', scope, date, resetSignal],
    queryFn: () => loadStarts(date!),
    enabled: !!date,
    staleTime: 0,
  })

  useEffect(() => {
    setStartMinute(null)
    setHours(null)
    if (resetSignal > 0) setDate(null)
  }, [resetSignal])

  const chosenStart: StartDto | undefined = starts.data?.starts.find((s) => s.startMinute === startMinute)

  useEffect(() => {
    onChange(date && startMinute != null && hours != null ? { businessDate: date, startMinute, hours, quantities } : null)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `onChange` is a setter of the owner; the pick is what matters
  }, [date, startMinute, hours, quantities])

  const { evening, afterMidnight } = useMemo(() => {
    const list = starts.data?.starts ?? []
    return { evening: list.filter((s) => s.startMinute < MINUTES_PER_DAY), afterMidnight: list.filter((s) => s.startMinute >= MINUTES_PER_DAY) }
  }, [starts.data])

  const pickDate = (d: string) => {
    setDate(d)
    setStartMinute(null)
    setHours(null)
  }
  const pickStart = (s: StartDto) => {
    setStartMinute(s.startMinute)
    setHours((h) => keepHours(h, s.options))
  }

  return (
    <div className="flex flex-col gap-6" data-testid="service-picker">
      <section aria-labelledby={`${scope}-step-date`}>
        <div className="mb-2 flex items-center justify-between gap-3">
          <h3 id={`${scope}-step-date`} className="text-[15px] font-semibold text-ink">
            1. Дата
          </h3>
          <div className="flex gap-1">
            <button
              type="button"
              disabled={offset === 0}
              onClick={() => setOffset((o) => Math.max(0, o - 1))}
              className="min-h-[44px] rounded-full border border-line bg-white px-4 text-sm font-medium text-ink-soft disabled:opacity-40"
            >
              Раньше
            </button>
            <button
              type="button"
              onClick={() => setOffset((o) => o + 1)}
              className="min-h-[44px] rounded-full border border-line bg-white px-4 text-sm font-medium text-ink"
            >
              Позже
            </button>
          </div>
        </div>
        {daysQuery.isLoading ? (
          <Skeleton className="h-32" />
        ) : daysQuery.isError || !days ? (
          <ErrorState message={getStayErrorMessage(daysQuery.error, 'Не удалось загрузить даты.')} onRetry={() => void daysQuery.refetch()} />
        ) : (
          <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
            {days.map((d) => {
              const selected = d.businessDate === date
              return (
                <li key={d.businessDate}>
                  <button
                    type="button"
                    disabled={!d.hasStarts}
                    aria-pressed={selected}
                    onClick={() => pickDate(d.businessDate)}
                    className={`flex min-h-[56px] w-full flex-col items-start justify-center rounded-2xl border px-3 py-2 text-left transition-colors ${
                      selected ? 'border-ink bg-ink text-cream' : d.hasStarts ? 'border-line bg-white text-ink hover:border-line-strong' : 'border-line bg-cream-deep text-muted'
                    }`}
                  >
                    <span className="text-sm font-semibold">{d.label}</span>
                    <span className="text-xs">{selected ? 'выбрано' : d.hasStarts ? 'есть время' : 'нет свободного времени'}</span>
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </section>

      {date && (
        <section aria-labelledby={`${scope}-step-start`} aria-live="polite">
          <h3 id={`${scope}-step-start`} className="mb-2 text-[15px] font-semibold text-ink">
            2. Время начала{starts.data ? ` — ${starts.data.dateLabel}` : ''}
          </h3>
          {starts.isLoading ? (
            <Skeleton className="h-24" />
          ) : starts.isError || !starts.data ? (
            <ErrorState message={getStayErrorMessage(starts.error, 'Не удалось загрузить время.')} onRetry={() => void starts.refetch()} />
          ) : starts.data.starts.length === 0 ? (
            <p role="status" className="rounded-2xl bg-cream-deep px-4 py-3 text-sm text-ink-soft">
              {starts.data.noStartsText ?? 'На эту дату свободного времени нет'}
            </p>
          ) : (
            <div className="flex flex-col gap-3">
              <StartGrid starts={evening} selected={startMinute} onPick={pickStart} />
              {afterMidnight.length > 0 && (
                <div>
                  <p className="mb-1.5 text-xs font-medium text-ink-soft">После полуночи</p>
                  <StartGrid starts={afterMidnight} selected={startMinute} onPick={pickStart} />
                </div>
              )}
            </div>
          )}
        </section>
      )}

      {chosenStart && (
        <section aria-labelledby={`${scope}-step-hours`}>
          <h3 id={`${scope}-step-hours`} className="mb-2 text-[15px] font-semibold text-ink">
            3. Сколько часов
          </h3>
          <ul className="flex flex-wrap gap-2">
            {chosenStart.options.map((o) => (
              <li key={o.hours}>
                <button
                  type="button"
                  aria-pressed={hours === o.hours}
                  onClick={() => setHours(o.hours)}
                  className={`min-h-[44px] rounded-2xl border px-4 py-2 text-left text-sm transition-colors ${
                    hours === o.hours ? 'border-ink bg-ink text-cream' : 'border-line bg-white text-ink hover:border-line-strong'
                  }`}
                >
                  <span className="font-semibold">{o.hours} ч</span>
                  <span className="block text-xs opacity-80">до {o.endLabel}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {hours != null && items.length > 0 && (
        <section aria-labelledby={`${scope}-step-items`}>
          <h3 id={`${scope}-step-items`} className="mb-1 text-[15px] font-semibold text-ink">
            4. Дополнительно
          </h3>
          <p className="mb-1 text-xs text-muted">По умолчанию ничего не добавлено — выберите только то, что нужно.</p>
          {items.map((it) => (
            <Stepper
              key={it.id}
              label={it.name}
              hint={`${it.priceRub === 0 ? 'бесплатно' : formatRub(it.priceRub)} · не больше ${it.maxPerSession}`}
              value={quantities[it.id] ?? 0}
              min={0}
              max={it.maxPerSession}
              onChange={(v) => setQuantities((q) => ({ ...q, [it.id]: clampQuantity(v, it.maxPerSession) }))}
            />
          ))}
        </section>
      )}
    </div>
  )
}

function StartGrid({ starts, selected, onPick }: { starts: readonly StartDto[]; selected: number | null; onPick: (s: StartDto) => void }) {
  return (
    <ul className="flex flex-wrap gap-2">
      {starts.map((s) => (
        <li key={s.startMinute}>
          <button
            type="button"
            aria-pressed={selected === s.startMinute}
            aria-label={`Начало в ${s.label}, до ${s.maxHours} ч`}
            onClick={() => onPick(s)}
            className={`min-h-[44px] min-w-[64px] rounded-2xl border px-3.5 py-2 text-sm font-semibold transition-colors ${
              selected === s.startMinute ? 'border-ink bg-ink text-cream' : 'border-line bg-white text-ink hover:border-line-strong'
            }`}
          >
            {s.label}
          </button>
        </li>
      ))}
    </ul>
  )
}
