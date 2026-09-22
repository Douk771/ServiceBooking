import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  format,
  startOfMonth,
  endOfMonth,
  eachDayOfInterval,
  getDay,
  addDays,
  addMonths,
  subMonths,
  isToday,
  isPast,
  isAfter,
  startOfDay,
} from 'date-fns'
import { ru } from 'date-fns/locale'
import { AxiosError } from 'axios'
import { bookingsApi, type DayAvailability } from '../../api/bookings'
import { parseHorizonExceededDays } from '../../utils/bookingHorizon'
import { Icon } from '../ui/Icon'

// US-65, §0.1 Q2/DAY_FULL_LABEL — customer's exact wording for "рабочий день, но свободных часов
// не осталось". Kept as one constant so a later wording change (customer signalled "Занято" might
// still change) is a one-line edit, not a hunt across the component.
export const DAY_FULL_LABEL = 'Занято'
const DAY_FULL_HINT = 'на этот день свободного времени не осталось'

const WEEK_DAYS = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']

function isoWeekday(d: Date) {
  const d0 = getDay(d)
  return d0 === 0 ? 7 : d0
}

const toDateStr = (d: Date) => format(d, 'yyyy-MM-dd')

interface Props {
  companyId: string
  masterId: string
  serviceId: string
  /** US-67: additional services beyond `serviceId` selected for this visit. `undefined` (embed
   *  widget) omits `serviceIds` from the request entirely — see `api/bookings.ts: serviceParams`. */
  extraServiceIds?: string[]
  selectedDate: string
  onSelectDate: (date: string) => void
}

export function BookingCalendar({ companyId, masterId, serviceId, extraServiceIds, selectedDate, onSelectDate }: Props) {
  const [month, setMonth] = useState(() => startOfMonth(new Date()))

  // API_CONTRACT_CYCLE6.md §41.2 / openapi-cycle6.yaml: the server rejects `from` earlier than
  // `today - 1` with a bare-string 400 ("from is too far in the past"). Requesting the 1st of the
  // CURRENT month — which is what "one request per displayed month" naturally asks for — trips that
  // rule on every day of the month except the 1st and 2nd, and the horizon retry below does not
  // apply (it only recognises the horizon message), so the whole calendar came back empty and
  // unclickable. Days before today are rendered from the browser's own local date anyway (the
  // `past` branch below), so they never needed server data: clamp the request's start to today.
  const monthStart = startOfMonth(month)
  const todayStart = startOfDay(new Date())
  const from = toDateStr(isAfter(todayStart, monthStart) ? todayStart : monthStart)
  const to = toDateStr(endOfMonth(month))
  const serviceKey = extraServiceIds ? [serviceId, ...extraServiceIds].join(',') : serviceId

  const { data, isLoading } = useQuery({
    queryKey: ['availability', companyId, masterId, serviceKey, from, to],
    // A company can set its booking horizon shorter than a month (§41.4, 1..365 days). Requesting
    // the whole displayed month can then legitimately overshoot `horizonLastDate` — which the
    // calendar can't know in advance, since that value only arrives IN the response. Rather than
    // guess a safe range up front, request the natural month and, on exactly this 400, clamp `to`
    // to what the server just told us and retry once.
    queryFn: async () => {
      try {
        return await bookingsApi.getAvailability(companyId, masterId, serviceId, extraServiceIds, from, to)
      } catch (err) {
        const ax = err as AxiosError
        const serverMsg = typeof ax?.response?.data === 'string' ? ax.response.data : ''
        const horizonDays = ax?.response?.status === 400 ? parseHorizonExceededDays(serverMsg) : null
        if (horizonDays == null) throw err

        const today = new Date()
        today.setHours(0, 0, 0, 0)
        const clampedTo = toDateStr(addDays(today, horizonDays))
        if (clampedTo >= to) throw err // clamped range isn't actually smaller — the 400 was for another reason

        return bookingsApi.getAvailability(companyId, masterId, serviceId, extraServiceIds, from, clampedTo)
      }
    },
    enabled: !!masterId && !!serviceId,
    staleTime: 0,
  })

  const statusMap = useMemo(() => {
    const map = new Map<string, DayAvailability>()
    data?.days.forEach((d) => map.set(d.date, d))
    return map
  }, [data])

  const days = eachDayOfInterval({ start: startOfMonth(month), end: endOfMonth(month) })
  const startPad = isoWeekday(startOfMonth(month)) - 1

  const horizonLastDate = data?.horizonLastDate ? new Date(`${data.horizonLastDate}T00:00:00`) : undefined
  const nextMonthStart = startOfMonth(addMonths(month, 1))
  const nextMonthDisabled = !!horizonLastDate && isAfter(nextMonthStart, horizonLastDate)
  // A month entirely in the past has nothing bookable in it, and asking for it would now send
  // `from` (clamped to today) after `to` (that month's end) — a different 400 from the same §41.2
  // rule set. Booking only ever goes forward, so the arrow is disabled on the current month.
  const prevMonthDisabled = !isAfter(monthStart, startOfMonth(new Date()))

  return (
    <div>
      {/* Month navigation */}
      <div className="flex items-center justify-between mb-3">
        <button
          type="button"
          aria-label="Предыдущий месяц"
          disabled={prevMonthDisabled}
          onClick={() => setMonth((m) => subMonths(m, 1))}
          className="w-[30px] h-[30px] rounded-full bg-cream-deep hover:bg-line flex items-center justify-center text-ink-soft transition-colors disabled:opacity-30 disabled:cursor-not-allowed disabled:hover:bg-cream-deep"
        >
          <Icon name="chevron-left" size={14} strokeWidth={1.8} />
        </button>
        <h4 className="text-[13.5px] font-semibold text-ink capitalize">
          {format(month, 'LLLL yyyy', { locale: ru })}
        </h4>
        <button
          type="button"
          aria-label="Следующий месяц"
          disabled={nextMonthDisabled}
          title={
            nextMonthDisabled && data
              ? `Записаться можно не дальше чем на ${data.horizonDays} дней вперёд`
              : undefined
          }
          onClick={() => setMonth((m) => addMonths(m, 1))}
          className="w-[30px] h-[30px] rounded-full bg-cream-deep hover:bg-line flex items-center justify-center text-ink-soft transition-colors disabled:opacity-30 disabled:cursor-not-allowed disabled:hover:bg-cream-deep"
        >
          <Icon name="chevron-right" size={14} strokeWidth={1.8} />
        </button>
      </div>

      {/* Weekday headers */}
      <div className="grid grid-cols-7 mb-1">
        {WEEK_DAYS.map((d) => (
          <div key={d} className="text-center text-[11px] font-medium text-muted py-1">
            {d}
          </div>
        ))}
      </div>

      {isLoading ? (
        <div className="grid grid-cols-7 gap-1">
          {Array.from({ length: 35 }).map((_, i) => (
            <div key={i} className="h-[46px] bg-cream-deep rounded-[10px] animate-pulse" />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-7 gap-1">
          {Array.from({ length: startPad }).map((_, i) => (
            <div key={`pad-${i}`} />
          ))}

          {days.map((day) => {
            const key = toDateStr(day)
            const entry = statusMap.get(key)
            const past = isPast(startOfDay(day)) && !isToday(day)
            const today = isToday(day)
            const beyondHorizon = !!horizonLastDate && isAfter(day, horizonLastDate)

            const status = entry?.status
            const clickable = !past && !beyondHorizon && status === 'Available'

            let cellClass =
              'relative flex flex-col items-center justify-center gap-0.5 rounded-[10px] h-[46px] text-sm transition-all select-none border '
            let label = ''

            if (past) {
              cellClass += 'border-transparent text-line-strong cursor-default'
            } else if (status === 'DayOff') {
              cellClass += 'bg-[#F5F2EC] border-line text-muted cursor-not-allowed'
              label = 'выходной'
            } else if (status === 'FullyBooked') {
              cellClass += 'bg-[#F5F2EC] border-line text-muted cursor-not-allowed'
              label = DAY_FULL_LABEL
            } else if (status === 'Available') {
              cellClass +=
                key === selectedDate
                  ? 'bg-ink text-cream border-ink cursor-pointer'
                  : 'bg-cream-deep border-line-strong hover:bg-line text-ink cursor-pointer'
            } else {
              // no data yet / beyond horizon
              cellClass += 'border-transparent text-line-strong cursor-not-allowed'
            }

            if (today) cellClass += ' ring-2 ring-gold-dark ring-offset-1'

            return (
              <button
                key={key}
                type="button"
                disabled={!clickable}
                aria-disabled={!clickable}
                aria-label={`${format(day, 'd MMMM', { locale: ru })}${label ? `, ${label}` : ''}`}
                title={status === 'FullyBooked' ? DAY_FULL_HINT : undefined}
                onClick={() => clickable && onSelectDate(key)}
                className={cellClass}
              >
                <span className="font-medium text-xs">{format(day, 'd')}</span>
                {label && <span className="text-[8.5px] leading-tight text-center px-1">{label}</span>}
              </button>
            )
          })}
        </div>
      )}

      {/* Legend */}
      <div className="flex flex-wrap items-center gap-3 mt-3 pt-3 border-t border-cream-deep">
        <div className="flex items-center gap-1.5">
          <div className="w-[11px] h-[11px] rounded-[3px] bg-cream-deep border border-line-strong" />
          <span className="text-[11px] text-ink-soft">Свободно</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-[11px] h-[11px] rounded-[3px] bg-[#F5F2EC] border border-line" />
          <span className="text-[11px] text-ink-soft">Выходной</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-[11px] h-[11px] rounded-[3px] bg-[#F5F2EC] border border-line" />
          <span className="text-[11px] text-ink-soft">{DAY_FULL_LABEL}</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-[11px] h-[11px] rounded-[3px] border border-transparent bg-cream text-line-strong" />
          <span className="text-[11px] text-ink-soft">Прошедший день</span>
        </div>
      </div>
    </div>
  )
}
