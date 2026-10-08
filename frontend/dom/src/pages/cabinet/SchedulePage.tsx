import { useQuery } from '@tanstack/react-query'
import { staysBoardApi } from '../../api/staysBoard'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { StayNotice } from '../../components/StayNotice'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { ScheduleArrivalDto, ScheduleDayDto } from '../../types'
import { can } from '../../utils/permissions'
import { SCHEDULE_DAYS, arrivalGuestsText, isQuietDay } from '../../utils/schedule'
import { getStayErrorMessage } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'

/**
 * `/cabinet/:companyId/schedule` (`ViewSchedule`, US-37-18) — cleanings and arrivals for today, tomorrow and the next two weeks. This is
 * the housekeeper's whole cabinet, so it is built for a 360 px phone: a day per section, a card per house. The data is the same form for every
 * role and has no phones, no sums, no files, no requisites; the guest's comment appears only when the owner allowed it.
 */
export function SchedulePage() {
  const { company } = useStaysCompany()
  const allowed = can(company.myPermissions, 'ViewSchedule')
  const q = useQuery({
    queryKey: ['stays-schedule', company.id],
    queryFn: () => staysBoardApi.schedule(company.id, { days: SCHEDULE_DAYS }),
    enabled: allowed,
    staleTime: 0,
    refetchInterval: 60_000,
  })
  if (!allowed) return <NotFoundPage title="Раздел недоступен" />

  return (
    <main className="mx-auto max-w-[760px] px-4 pb-10 pt-6 sm:px-8">
      <h2 className="mb-1 font-serif text-[26px] text-ink">График</h2>
      <p className="mb-5 text-sm text-ink-soft">Уборки и заезды на ближайшие две недели.</p>
      {can(company.myPermissions, 'ViewBookings') && <StayNotice textKey="StayMigrationOwnerNotice" className="mb-5" />}

      {q.isLoading ? (
        <LoadingList rows={3} rowClass="h-28" />
      ) : q.isError ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить график.')} onRetry={() => void q.refetch()} />
      ) : !q.data || q.data.days.length === 0 ? (
        <EmptyState title="График пуст" text="Как только появятся брони, здесь будут выезды и заезды по дням." />
      ) : (
        <div className="flex flex-col gap-6">
          {q.data.days.map((d) => (
            <DaySection key={d.date} day={d} today={q.data.today} />
          ))}
        </div>
      )}
    </main>
  )
}

function DaySection({ day, today }: { day: ScheduleDayDto; today: string }) {
  return (
    <section aria-labelledby={`d-${day.date}`}>
      <h3 id={`d-${day.date}`} className={`mb-2 text-[15px] font-semibold ${day.date === today ? 'text-gold-dark' : 'text-ink'}`}>
        {day.label}
      </h3>
      {isQuietDay(day) ? (
        <p className="rounded-2xl border border-dashed border-line px-4 py-3 text-sm text-muted">Выездов и заездов нет</p>
      ) : (
        <ul className="flex flex-col gap-2.5">
          {day.departures.map((x) => (
            <li key={`out-${x.bookingId}`} className="rounded-2xl border border-line bg-white px-4 py-3">
              <p className="text-sm font-semibold text-ink">
                Выезд до {x.checkOutTime} · {x.houseName}
              </p>
              {x.sameDayTurnover && <p className="mt-1 text-xs font-medium text-gold-dark">В этот же день заезд — уборка между выездом и заездом</p>}
            </li>
          ))}
          {day.arrivals.map((a) => (
            <ArrivalCard key={`in-${a.bookingId}`} a={a} />
          ))}
        </ul>
      )}
    </section>
  )
}

function ArrivalCard({ a }: { a: ScheduleArrivalDto }) {
  return (
    <li className="rounded-2xl border border-line bg-white px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-semibold text-ink">
          Заезд с {a.checkInTime} · {a.houseName}
        </p>
        {a.paymentUnconfirmed && <span className="rounded-full bg-warning-bg px-2.5 py-0.5 text-xs font-semibold text-warning">оплата не подтверждена</span>}
      </div>
      {a.turnoverText && <p className="mt-1 text-xs font-medium text-gold-dark">{a.turnoverText}</p>}
      <p className="mt-1.5 text-sm text-ink">
        {a.guestName ?? 'Гость'}
        {a.arrivalTime ? ` · приедет около ${a.arrivalTime}` : ''}
      </p>
      <p className="text-sm text-ink-soft">{arrivalGuestsText(a)}</p>
      {a.comment && <p className="mt-1.5 whitespace-pre-line rounded-xl bg-cream-deep px-3 py-2 text-sm text-ink-soft">Комментарий: {a.comment}</p>}
    </li>
  )
}
