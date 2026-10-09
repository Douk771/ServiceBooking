import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { CabinetServiceDayView } from '@/components/slots/views/CabinetServiceDayView'
import { useSlotVertical } from '@/components/slots/SlotVerticalContext'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { fmtDateTime } from '@/utils/dateFormat'
import { can } from '@/utils/slots/slotPermissions'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { useBathsCompany } from '../../cabinet/cabinetVertical'

const LIST_SIZE = 5

/**
 * `/cabinet/:companyId/service-day/:date` — the main screen of the cabinet (US-42-07): the counter «Ожидают проверки оплаты», the shared
 * «День услуг» (a scale on a tablet, a list per resource on a phone) and the first bookings that wait for a check. Re-read by the
 * revision poll of `CompanyLayout`; the day also polls itself.
 */
export function DayPage() {
  const { company } = useBathsCompany()
  const { api, paths } = useSlotVertical()
  const allowed = can(company.myPermissions, 'ViewBookings')
  const awaiting = company.awaitingPaymentCount ?? 0
  const q = useQuery({
    queryKey: ['stays-service-sessions', company.id, 'AwaitingPaymentCheck', 1, 'day'],
    queryFn: () => api.sessions.sessions(company.id, { status: 'AwaitingPaymentCheck', page: 1, pageSize: LIST_SIZE }),
    enabled: allowed,
    staleTime: 0,
    placeholderData: (prev) => prev,
  })

  return (
    <>
      {allowed && (
        <div className="mx-auto max-w-[1280px] px-4 pt-6 sm:px-8">
          <Link
            to={`/cabinet/${company.id}/orders`}
            data-testid="awaiting-counter"
            className={`flex min-h-[56px] flex-wrap items-center justify-between gap-2 rounded-2xl border px-5 py-3 !text-ink ${awaiting > 0 ? 'border-gold bg-white' : 'border-line bg-white/60'}`}
          >
            <span className="text-sm font-semibold">Ожидают проверки оплаты</span>
            <span className={`text-2xl font-semibold tabular-nums ${awaiting > 0 ? 'text-gold-dark' : 'text-muted'}`}>{awaiting}</span>
          </Link>
        </div>
      )}

      <CabinetServiceDayView />

      {allowed && (
        <section aria-labelledby="day-queue-h" className="mx-auto max-w-[1280px] px-4 pb-10 sm:px-8">
          <div className="mb-3 flex items-center justify-between gap-3">
            <h3 id="day-queue-h" className="font-serif text-xl text-ink">
              Брони, которые ждут проверки
            </h3>
            <Link to={`/cabinet/${company.id}/orders`} className="min-h-[44px] content-center text-sm font-semibold text-gold-dark hover:underline">
              Все брони
            </Link>
          </div>
          {q.isLoading ? (
            <LoadingList rows={2} />
          ) : q.isError && !q.data ? (
            <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить брони.')} onRetry={() => void q.refetch()} />
          ) : !q.data || q.data.items.length === 0 ? (
            <EmptyState title="Проверять нечего" text="Когда гость приложит подтверждение оплаты, бронь появится здесь." />
          ) : (
            <ul className="flex flex-col gap-2.5">
              {q.data.items.map((s) => (
                <li key={s.id}>
                  <Link to={paths.cabinetSession(company.id, s.id)} className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-line bg-white px-4 py-3 !text-ink hover:bg-cream-deep/30">
                    <span className="min-w-0">
                      <span className="block truncate text-sm font-semibold">{s.serviceName}</span>
                      <span className="block text-xs text-ink-soft">
                        {s.time.label} · {s.guestName || 'Гость'}
                      </span>
                    </span>
                    {s.firstProofUploadedAtUtc && <span className="text-xs text-gold-dark">Файл приложен {fmtDateTime(s.firstProofUploadedAtUtc)}</span>}
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </>
  )
}
