import { useQuery } from '@tanstack/react-query'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { can } from '@/utils/slots/slotPermissions'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { bathsCabinetApi } from '../../api/bathsCabinet'
import { useBathsCompany } from '../../cabinet/cabinetVertical'
import { SCHEDULE_DAYS, guestLine, itemsText } from '../../cabinet/schedule'
import type { BathScheduleDayDto, BathScheduleSessionDto } from '../../cabinet/types'
import { NotFoundPage } from '../NotFoundPage'

/**
 * `/cabinet/:companyId/schedule` (`ViewSchedule`, US-42-19/21) — the bather's whole cabinet: which bath, when, who, how many, what to
 * prepare. Built for a 360 px phone: a day per section, a card per session. The server's form is CLOSED — no phone, no sums, no files, no
 * requisites, no payment status (only «оплата не подтверждена»); the guest's comment arrives only when the owner allowed it.
 */
export function SchedulePage() {
  const { company } = useBathsCompany()
  const allowed = can(company.myPermissions, 'ViewSchedule')
  const q = useQuery({
    queryKey: ['baths-schedule', company.id],
    queryFn: () => bathsCabinetApi.schedule(company.id, { days: SCHEDULE_DAYS }),
    enabled: allowed,
    staleTime: 0,
    refetchInterval: 60_000,
  })
  if (!allowed) return <NotFoundPage title="Раздел недоступен" />

  return (
    <main className="mx-auto max-w-[760px] px-4 pb-10 pt-6 sm:px-8">
      <h2 className="mb-1 font-serif text-[26px] text-ink">Расписание</h2>
      <p className="mb-5 text-sm text-ink-soft">Сеансы на ближайшие две недели.</p>
      {q.isLoading ? (
        <LoadingList rows={3} rowClass="h-28" />
      ) : q.isError && !q.data ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить расписание.')} onRetry={() => void q.refetch()} />
      ) : !q.data || q.data.days.every((d) => d.sessions.length === 0) ? (
        <EmptyState title="Сеансов нет" text="Как только появятся брони, здесь будут сеансы по дням." />
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

function DaySection({ day, today }: { day: BathScheduleDayDto; today: string }) {
  return (
    <section aria-labelledby={`d-${day.date}`}>
      <h3 id={`d-${day.date}`} className={`mb-2 text-[15px] font-semibold ${day.date === today ? 'text-gold-dark' : 'text-ink'}`}>
        {day.label}
      </h3>
      {day.sessions.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-line px-4 py-3 text-sm text-muted">Сеансов нет</p>
      ) : (
        <ul className="flex flex-col gap-2.5">
          {day.sessions.map((s) => (
            <SessionCard key={s.sessionId} s={s} />
          ))}
        </ul>
      )}
    </section>
  )
}

function SessionCard({ s }: { s: BathScheduleSessionDto }) {
  return (
    <li className="rounded-2xl border border-line bg-white px-4 py-3" data-testid="schedule-session">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="min-w-0 break-words text-sm font-semibold text-ink">{s.serviceName}</p>
        {s.paymentUnconfirmed && <span className="rounded-full bg-warning-bg px-2.5 py-0.5 text-xs font-semibold text-warning">оплата не подтверждена</span>}
      </div>
      <p className="mt-1 text-sm text-ink">{s.timeLabel}</p>
      <p className="mt-0.5 text-xs text-ink-soft">Подготовка — до {s.preparedUntilLabel}</p>
      <p className="mt-1.5 text-sm text-ink">{guestLine(s)}</p>
      {s.items.length > 0 && <p className="text-sm text-ink-soft">{itemsText(s.items)}</p>}
      {s.comment && <p className="mt-1.5 whitespace-pre-line break-words rounded-xl bg-cream-deep px-3 py-2 text-sm text-ink-soft">Комментарий: {s.comment}</p>}
    </li>
  )
}
