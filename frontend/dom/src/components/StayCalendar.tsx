import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { Icon } from '@/components/ui/Icon'
import { useMediaQuery } from '../hooks/useMediaQuery'
import type { HouseCalendarDto } from '../types'
import {
  WEEKDAY_HEADERS,
  addDays,
  addMonths,
  daysInMonth,
  firstOfMonth,
  formatDateWithWeekday,
  formatMonthTitle,
  weekdayMon0,
} from '../utils/stayDates'
import { dayStateMap } from '../utils/stayRules'
import { dayStateText, pickDay, priceShort, type StayRange } from '../utils/staySelection'

interface Props {
  calendar: HouseCalendarDto
  value: StayRange
  onChange: (range: StayRange) => void
  /** Called with the reason when a click did not complete a range (also shown under the calendar). */
  onMessage?: (message: string | null) => void
}

/**
 * Date picker of the booking (US-37-05/06, ARCHITECTURE_CYCLE37.md §37.14.5). The state of a night is a WORD (aria-label and
 * the legend), never colour alone; cells are 44×44 px; the grid is driven from the keyboard (arrows, Home/End, PageUp/PageDown,
 * Enter/Space, Esc). The server's calendar is the only source of states (`GET …/calendar`); the range rules are `staySelection`.
 */
export function StayCalendar({ calendar, value, onChange, onMessage }: Props) {
  const twoMonths = useMediaQuery('(min-width: 768px)')
  const states = useMemo(() => dayStateMap(calendar), [calendar])
  const firstMonth = firstOfMonth(calendar.from)
  const lastMonth = firstOfMonth(addDays(calendar.lastNight, 1))
  const [month, setMonth] = useState(() => firstOfMonth(value.checkIn ?? calendar.today))
  const [focusDate, setFocusDate] = useState<string>(() => value.checkIn ?? calendar.today)
  const [message, setMessage] = useState<string | null>(null)
  const gridRef = useRef<HTMLDivElement>(null)
  const pendingFocus = useRef(false)

  const shown = twoMonths ? [month, addMonths(month, 1)] : [month]
  const canPrev = month > firstMonth
  const canNext = addMonths(month, shown.length) <= lastMonth

  // Moving the roving focus into a month that is not on screen scrolls the pair of months to it.
  useEffect(() => {
    if (pendingFocus.current) {
      pendingFocus.current = false
      gridRef.current?.querySelector<HTMLButtonElement>(`[data-date="${focusDate}"]`)?.focus()
    }
  }, [focusDate, month])

  const emit = (msg: string | null) => {
    setMessage(msg)
    onMessage?.(msg)
  }

  const select = (date: string) => {
    const r = pickDay(calendar, value, date)
    emit(r.message)
    if (r.range !== value) onChange(r.range)
  }

  const moveFocus = (date: string) => {
    const bounded = date < calendar.from ? calendar.from : date > addDays(calendar.lastNight, 1) ? addDays(calendar.lastNight, 1) : date
    pendingFocus.current = true
    setFocusDate(bounded)
    const m = firstOfMonth(bounded)
    if (m < month) setMonth(m)
    else if (m >= addMonths(month, shown.length)) setMonth(addMonths(m, -(shown.length - 1)))
  }

  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>, date: string) => {
    const step: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }
    if (e.key in step) {
      e.preventDefault()
      moveFocus(addDays(date, step[e.key]))
    } else if (e.key === 'Home') {
      e.preventDefault()
      moveFocus(addDays(date, -weekdayMon0(date)))
    } else if (e.key === 'End') {
      e.preventDefault()
      moveFocus(addDays(date, 6 - weekdayMon0(date)))
    } else if (e.key === 'PageUp' || e.key === 'PageDown') {
      e.preventDefault()
      const dir = e.key === 'PageUp' ? -1 : 1
      const target = addMonths(firstOfMonth(date), dir)
      const day = Math.min(Number(date.slice(8)), daysInMonth(target))
      moveFocus(`${target.slice(0, 8)}${String(day).padStart(2, '0')}`)
    } else if (e.key === 'Escape') {
      e.preventDefault()
      emit(null)
      onChange({ checkIn: null, checkOut: null })
    }
  }

  const hint = !value.checkIn ? 'Выберите дату заезда' : !value.checkOut ? 'Теперь выберите дату выезда' : null

  return (
    <div className="select-none" data-testid="stay-calendar">
      <div className="mb-3 flex items-center justify-between gap-2">
        <button
          type="button"
          onClick={() => setMonth(addMonths(month, -1))}
          disabled={!canPrev}
          aria-label="Предыдущий месяц"
          className="flex h-11 w-11 items-center justify-center rounded-full border border-line text-ink-soft hover:border-line-strong disabled:opacity-35"
        >
          <Icon name="chevron-left" size={16} strokeWidth={1.8} />
        </button>
        <p className="text-center text-sm font-semibold text-ink" aria-live="polite">
          {shown.map(formatMonthTitle).join(' — ')}
        </p>
        <button
          type="button"
          onClick={() => setMonth(addMonths(month, 1))}
          disabled={!canNext}
          aria-label="Следующий месяц"
          className="flex h-11 w-11 items-center justify-center rounded-full border border-line text-ink-soft hover:border-line-strong disabled:opacity-35"
        >
          <Icon name="chevron-right" size={16} strokeWidth={1.8} />
        </button>
      </div>

      <div ref={gridRef} className={`grid gap-x-8 gap-y-5 ${twoMonths ? 'grid-cols-2' : 'grid-cols-1'}`}>
        {shown.map((m) => (
          <MonthGrid
            key={m}
            month={m}
            calendar={calendar}
            states={states}
            value={value}
            focusDate={focusDate}
            onFocusDate={setFocusDate}
            onSelect={select}
            onKeyDown={onKeyDown}
          />
        ))}
      </div>

      <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-[11px] text-ink-soft" aria-label="Обозначения">
        <li className="flex items-center gap-1.5">
          <span className="h-3.5 w-3.5 rounded border border-line-strong bg-white" aria-hidden="true" /> Свободно
        </li>
        <li className="flex items-center gap-1.5">
          <span className="h-3.5 w-3.5 rounded border border-dashed border-gold bg-warning-bg" aria-hidden="true" /> Возможно освободится
        </li>
        <li className="flex items-center gap-1.5">
          <span className="h-3.5 w-3.5 rounded bg-cream-deep text-[9px] leading-[14px] text-muted" aria-hidden="true">
            ×
          </span>{' '}
          Занято
        </li>
        <li className="flex items-center gap-1.5">
          <span className="h-3.5 w-3.5 rounded bg-ink" aria-hidden="true" /> Ваши даты
        </li>
      </ul>

      <p className="mt-2 min-h-[20px] text-xs" role="status" aria-live="polite">
        {message ? <span className="text-danger">{message}</span> : hint ? <span className="text-ink-soft">{hint}</span> : null}
      </p>
    </div>
  )
}

function MonthGrid({
  month,
  calendar,
  states,
  value,
  focusDate,
  onFocusDate,
  onSelect,
  onKeyDown,
}: {
  month: string
  calendar: HouseCalendarDto
  states: ReturnType<typeof dayStateMap>
  value: StayRange
  focusDate: string
  onFocusDate: (d: string) => void
  onSelect: (d: string) => void
  onKeyDown: (e: KeyboardEvent<HTMLButtonElement>, d: string) => void
}) {
  const lead = weekdayMon0(month)
  const total = daysInMonth(month)
  const cells: (string | null)[] = [...Array(lead).fill(null), ...Array.from({ length: total }, (_, i) => addDays(month, i))]
  while (cells.length % 7 !== 0) cells.push(null)
  const weeks: (string | null)[][] = []
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7))

  // One tab stop per grid: the focus date when it is in this month, otherwise the first selectable cell of the month.
  const tabDate = focusDate.startsWith(month.slice(0, 7)) ? focusDate : null

  return (
    <div role="grid" aria-label={formatMonthTitle(month)}>
      <div role="row" className="mb-1 grid grid-cols-7 text-center text-[11px] font-medium uppercase tracking-wide text-muted">
        {WEEKDAY_HEADERS.map((w) => (
          <span key={w} role="columnheader" className="py-1">
            {w}
          </span>
        ))}
      </div>
      {weeks.map((week, wi) => (
        <div role="row" key={wi} className="grid grid-cols-7">
          {week.map((date, di) =>
            date === null ? (
              <span key={`e${di}`} role="gridcell" aria-hidden="true" />
            ) : (
              <DayCell
                key={date}
                date={date}
                calendar={calendar}
                dayState={states.get(date)}
                value={value}
                tabbable={tabDate === date || (tabDate === null && date === month)}
                onFocusDate={onFocusDate}
                onSelect={onSelect}
                onKeyDown={onKeyDown}
              />
            ),
          )}
        </div>
      ))}
    </div>
  )
}

function DayCell({
  date,
  calendar,
  dayState,
  value,
  tabbable,
  onFocusDate,
  onSelect,
  onKeyDown,
}: {
  date: string
  calendar: HouseCalendarDto
  dayState: HouseCalendarDto['days'][number] | undefined
  value: StayRange
  tabbable: boolean
  onFocusDate: (d: string) => void
  onSelect: (d: string) => void
  onKeyDown: (e: KeyboardEvent<HTMLButtonElement>, d: string) => void
}) {
  const state = dayState?.state ?? 'Unavailable'
  const isStart = value.checkIn === date
  const isEnd = value.checkOut === date
  const inside = !!value.checkIn && !!value.checkOut && date > value.checkIn && date < value.checkOut
  const picked = isStart || isEnd
  const price = dayState?.priceRub ?? null
  const dayNum = Number(date.slice(8))

  const parts = [formatDateWithWeekday(date), dayStateText(state)]
  if (state === 'Free' && price != null) parts.push(`${price.toLocaleString('ru-RU')} ₽ за ночь`)
  if (isStart) parts.push('дата заезда')
  if (isEnd) parts.push('дата выезда')
  if (date === calendar.today) parts.push('сегодня')

  let look = 'bg-white text-ink hover:border-gold'
  if (state === 'MayFreeUp') look = 'border-dashed !border-gold bg-warning-bg text-ink-soft'
  else if (state === 'Occupied') look = 'bg-cream-deep text-muted line-through decoration-muted/70'
  else if (state === 'Unavailable') look = 'bg-transparent text-line-strong'
  if (inside) look = 'bg-gold/15 text-ink'
  if (picked) look = 'bg-ink !text-cream'

  return (
    <div role="gridcell" aria-selected={picked || inside} className="p-px">
      <button
        type="button"
        data-date={date}
        tabIndex={tabbable ? 0 : -1}
        aria-label={parts.join(', ')}
        aria-pressed={picked}
        onFocus={() => onFocusDate(date)}
        onClick={() => onSelect(date)}
        onKeyDown={(e) => onKeyDown(e, date)}
        className={`flex min-h-[44px] w-full flex-col items-center justify-center rounded-lg border border-transparent text-sm leading-none transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-gold ${look}`}
      >
        <span className="font-medium">{dayNum}</span>
        {state === 'Free' && price != null && !picked && <span className="mt-0.5 text-[9px] text-muted">{priceShort(price)}</span>}
        {state === 'MayFreeUp' && <span className="mt-0.5 text-[9px]" aria-hidden="true">···</span>}
      </button>
    </div>
  )
}
