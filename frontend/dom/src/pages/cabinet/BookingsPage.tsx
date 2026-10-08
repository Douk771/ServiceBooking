import { useMemo } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Pagination } from '@/components/ui/Pagination'
import { fmtDateTime } from '@/utils/dateFormat'
import { formatPhone, telHref } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import { staysBoardApi } from '../../api/staysBoard'
import { staysHousesApi } from '../../api/staysHouses'
import { SessionsList } from '../../components/services/staff/SessionsList'
import { StatusBadge } from '../../components/StatusBadge'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { StaffStayBookingListItemDto } from '../../types'
import {
  BOOKINGS_PAGE_SIZE,
  BOOKINGS_POLL_MS,
  PRESETS,
  isQueue,
  presetOf,
  statusesOf,
} from '../../utils/bookingFilters'
import { can } from '../../utils/permissions'
import { formatInstantInZone, formatStayRange } from '../../utils/stayDates'
import { getStayErrorMessage } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'

/**
 * `/cabinet/:companyId/bookings` (`ViewBookings`, US-37-17) — «Ожидают проверки оплаты» by default (the owner's queue, oldest file first)
 * and every booking with a status filter. The filter and the page live in the URL. The queue is polled every 15 s.
 */
export function BookingsPage() {
  const { company } = useStaysCompany()
  const [sp, setSp] = useSearchParams()
  const view = sp.get('view') === 'sessions' ? 'sessions' : 'bookings'
  const preset = presetOf(sp.get('status'))
  const houseId = sp.get('house') ?? ''
  const page = Math.max(1, Number(sp.get('page')) || 1)
  const statuses = statusesOf(preset)

  const houses = useQuery({
    queryKey: ['stays-houses', company.id],
    queryFn: () => staysHousesApi.list(company.id),
    staleTime: 60_000,
  })
  const q = useQuery({
    queryKey: ['stays-bookings', company.id, preset, houseId, page],
    queryFn: () =>
      staysBoardApi.bookings(company.id, {
        status: statuses,
        houseId: houseId || undefined,
        page,
        pageSize: BOOKINGS_PAGE_SIZE,
      }),
    refetchInterval: isQueue(preset) ? BOOKINGS_POLL_MS : false,
    staleTime: 0,
    placeholderData: (prev) => prev,
    enabled: can(company.myPermissions, 'ViewBookings') && view === 'bookings',
  })
  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(sp)
    for (const [k, v] of Object.entries(patch)) {
      if (v === null || v === '') next.delete(k)
      else next.set(k, v)
    }
    setSp(next)
  }
  const empty = useMemo(() => PRESETS.find((p) => p.id === preset)!.empty, [preset])

  if (!can(company.myPermissions, 'ViewBookings')) return <NotFoundPage title="Раздел недоступен" />

  const list = q.data
  return (
    <main className="mx-auto max-w-[960px] px-4 pb-8 pt-6 sm:px-8">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <h2 className="font-serif text-[26px] text-ink">Брони</h2>
        {(houses.data ?? []).length > 1 && (
          <>
            <label htmlFor="bk-house" className="sr-only">
              Дом
            </label>
            <select
              id="bk-house"
              value={houseId}
              onChange={(e) => set({ house: e.target.value, page: null })}
              className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink"
            >
              <option value="">Все дома</option>
              {houses.data!.map((h) => (
                <option key={h.id} value={h.id}>
                  {h.name}
                </option>
              ))}
            </select>
          </>
        )}
      </div>

      <div role="tablist" aria-label="Что показать" className="mb-4 flex gap-2">
        {[
          { id: 'bookings', label: 'Брони домов' },
          { id: 'sessions', label: 'Сеансы услуг' },
        ].map((v) => (
          <button
            key={v.id}
            role="tab"
            type="button"
            aria-selected={view === v.id}
            onClick={() => set({ view: v.id === 'bookings' ? null : v.id })}
            className={`min-h-[44px] border-b-2 px-3.5 text-sm font-semibold transition-colors ${view === v.id ? 'border-ink text-ink' : 'border-transparent text-ink-soft hover:text-ink'}`}
          >
            {v.label}
          </button>
        ))}
      </div>

      {view === 'sessions' ? (
        <SessionsList companyId={company.id} awaitingCount={company.awaitingPaymentCount ?? undefined} />
      ) : (
        <>
          <div
            role="tablist"
            aria-label="Статус брони"
            className="-mx-4 mb-5 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0"
          >
            {PRESETS.map((p) => (
              <button
                key={p.id}
                role="tab"
                type="button"
                aria-selected={preset === p.id}
                onClick={() => set({ status: p.id === 'awaiting' ? null : p.id, page: null })}
                className={`inline-flex min-h-[44px] shrink-0 items-center gap-2 rounded-full border px-4 text-sm font-medium transition-colors ${preset === p.id ? 'border-ink bg-ink text-cream' : 'border-line bg-white text-ink-soft hover:border-line-strong'}`}
              >
                {p.label}
                {p.id === 'awaiting' && !!company.awaitingPaymentCount && company.awaitingPaymentCount > 0 && (
                  <span
                    className={`rounded-full px-1.5 text-[11px] font-bold leading-5 ${preset === p.id ? 'bg-white/25 text-white' : 'bg-gold text-white'}`}
                  >
                    {company.awaitingPaymentCount}
                  </span>
                )}
              </button>
            ))}
          </div>

          {q.isLoading ? (
            <LoadingList rows={4} />
          ) : q.isError && !list ? (
            <ErrorState
              message={getStayErrorMessage(q.error, 'Не удалось загрузить брони.')}
              onRetry={() => void q.refetch()}
            />
          ) : !list || list.items.length === 0 ? (
            <EmptyState title={empty} />
          ) : (
            <>
              <ul className="flex flex-col gap-3" aria-live="polite">
                {list.items.map((b) => (
                  <BookingRow key={b.id} b={b} companyId={company.id} tz={company.timeZoneId} queue={isQueue(preset)} />
                ))}
              </ul>
              <Pagination
                page={list.page}
                pageSize={list.pageSize || BOOKINGS_PAGE_SIZE}
                total={list.totalCount}
                hasNext={list.page * (list.pageSize || BOOKINGS_PAGE_SIZE) < list.totalCount}
                onPageChange={(n) => set({ page: n === 1 ? null : String(n) })}
              />
            </>
          )}
        </>
      )}
    </main>
  )
}

function BookingRow({
  b,
  companyId,
  tz,
  queue,
}: {
  b: StaffStayBookingListItemDto
  companyId: string
  tz: string
  queue: boolean
}) {
  return (
    <li className="rounded-2xl border border-line bg-white">
      <Link
        to={`/cabinet/${companyId}/bookings/${b.id}`}
        className="flex flex-wrap items-start justify-between gap-3 p-4 !text-ink hover:bg-cream-deep/30 sm:p-5"
      >
        <div className="min-w-0">
          <p className="font-semibold">
            {b.guestName || 'Гость'}
            {b.isManual && (
              <span className="ml-2 rounded-full bg-cream-deep px-2 py-0.5 text-[11px] font-medium text-ink-soft">
                вручную
              </span>
            )}
          </p>
          <p className="mt-0.5 text-sm text-ink-soft">
            {b.houseName} · {formatStayRange(b.checkInDate, b.checkOutDate)}
          </p>
          {b.guestPhone && <p className="mt-0.5 text-xs text-muted">{formatPhone(b.guestPhone)}</p>}
          {queue && b.firstProofUploadedAtUtc && (
            <p className="mt-1 text-xs text-gold-dark">Файл приложен {fmtDateTime(b.firstProofUploadedAtUtc)}</p>
          )}
          {b.status === 'Held' && b.holdExpiresAtUtc && (
            <p className="mt-1 text-xs text-warning">Ждём оплату до {formatInstantInZone(b.holdExpiresAtUtc, tz)}</p>
          )}
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1.5 text-right">
          <StatusBadge status={b.displayStatus} text={b.statusText} />
          <p className="text-sm font-semibold text-ink">{formatRub(b.totalRub)}</p>
          {b.prepayRub > 0 && <p className="text-xs text-muted">предоплата {formatRub(b.prepayRub)}</p>}
        </div>
      </Link>
      {b.guestPhone && (
        <div className="border-t border-line/70 px-4 py-1 sm:px-5">
          <a
            href={telHref(b.guestPhone) || undefined}
            className="inline-flex min-h-[44px] items-center text-sm font-medium !text-gold-dark"
          >
            Позвонить гостю
          </a>
        </div>
      )}
    </li>
  )
}
