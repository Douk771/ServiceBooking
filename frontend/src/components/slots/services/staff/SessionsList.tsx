import { Link, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Pagination } from '@/components/ui/Pagination'
import { fmtDateTime } from '@/utils/dateFormat'
import { formatPhone } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import type { StaffServiceSessionListItemDto } from '@/types/slots'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { useSlotVertical } from '@/components/slots/SlotVerticalContext'

const PAGE_SIZE = 20
const POLL_MS = 15_000

const SESSION_PRESETS = [
  { id: 'AwaitingPaymentCheck', label: 'Ожидают проверки оплаты', empty: 'Нет заказов услуг, ожидающих проверки оплаты' },
  { id: 'Held', label: 'Ждут оплаты', empty: 'Нет заказов услуг, которые ждут оплаты' },
  { id: 'Confirmed', label: 'Подтверждённые', empty: 'Нет подтверждённых заказов услуг' },
] as const
type PresetId = (typeof SESSION_PRESETS)[number]['id']
const presetOf = (v: string | null): PresetId => SESSION_PRESETS.find((p) => p.id === v)?.id ?? 'AwaitingPaymentCheck'

/**
 * «Сеансы» in the list of bookings (US-39-14): separate orders of services in the same working queue as the bookings — «Ожидают проверки
 * оплаты» first, the oldest file first (the server sorts). Each row opens the card of the session.
 */
export function SessionsList({ companyId, awaitingCount }: { companyId: string; awaitingCount?: number }) {
  const staysBoardApi = useSlotVertical().api.sessions
  const [sp, setSp] = useSearchParams()
  const preset = presetOf(sp.get('sstatus'))
  const page = Math.max(1, Number(sp.get('spage')) || 1)
  const q = useQuery({
    queryKey: ['stays-service-sessions', companyId, preset, page],
    queryFn: () => staysBoardApi.sessions(companyId, { status: preset, page, pageSize: PAGE_SIZE }),
    refetchInterval: preset === 'AwaitingPaymentCheck' ? POLL_MS : false,
    staleTime: 0,
    placeholderData: (prev) => prev,
  })
  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(sp)
    for (const [k, v] of Object.entries(patch)) {
      if (v === null || v === '') next.delete(k)
      else next.set(k, v)
    }
    setSp(next)
  }
  const list = q.data
  const empty = SESSION_PRESETS.find((p) => p.id === preset)!.empty

  return (
    <div data-testid="sessions-list">
      <div role="tablist" aria-label="Статус заказа услуги" className="-mx-4 mb-5 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0">
        {SESSION_PRESETS.map((p) => (
          <button
            key={p.id}
            role="tab"
            type="button"
            aria-selected={preset === p.id}
            onClick={() => set({ sstatus: p.id === 'AwaitingPaymentCheck' ? null : p.id, spage: null })}
            className={`inline-flex min-h-[44px] shrink-0 items-center gap-2 rounded-full border px-4 text-sm font-medium transition-colors ${preset === p.id ? 'border-ink bg-ink text-cream' : 'border-line bg-white text-ink-soft hover:border-line-strong'}`}
          >
            {p.label}
            {p.id === 'AwaitingPaymentCheck' && !!awaitingCount && awaitingCount > 0 && <span className="sr-only"> (всего ожидают проверки: {awaitingCount})</span>}
          </button>
        ))}
      </div>

      {q.isLoading ? (
        <LoadingList rows={4} />
      ) : q.isError && !list ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить заказы услуг.')} onRetry={() => void q.refetch()} />
      ) : !list || list.items.length === 0 ? (
        <EmptyState title={empty} />
      ) : (
        <>
          <ul className="flex flex-col gap-3" aria-live="polite">
            {list.items.map((s) => (
              <SessionRow key={s.id} s={s} companyId={companyId} queue={preset === 'AwaitingPaymentCheck'} />
            ))}
          </ul>
          <Pagination
            page={list.page}
            pageSize={list.pageSize || PAGE_SIZE}
            total={list.totalCount}
            hasNext={list.page * (list.pageSize || PAGE_SIZE) < list.totalCount}
            onPageChange={(n) => set({ spage: n === 1 ? null : String(n) })}
          />
        </>
      )}
    </div>
  )
}

function SessionRow({ s, companyId, queue }: { s: StaffServiceSessionListItemDto; companyId: string; queue: boolean }) {
  const paths = useSlotVertical().paths
  return (
    <li className="rounded-2xl border border-line bg-white">
      <Link to={paths.cabinetSession(companyId, s.id)} className="flex flex-wrap items-start justify-between gap-3 p-4 !text-ink hover:bg-cream-deep/30 sm:p-5">
        <div className="min-w-0">
          <p className="font-semibold">{s.serviceName}</p>
          <p className="mt-0.5 text-sm text-ink-soft">{s.time.label}</p>
          <p className="mt-0.5 text-sm text-ink-soft">
            {s.guestName || 'Гость'}
            {s.houseName ? ` · ${s.houseName}` : ' · без проживания'}
          </p>
          {s.guestPhone && <p className="mt-0.5 text-xs text-muted">{formatPhone(s.guestPhone)}</p>}
          {queue && s.firstProofUploadedAtUtc && <p className="mt-1 text-xs text-gold-dark">Файл приложен {fmtDateTime(s.firstProofUploadedAtUtc)}</p>}
          {s.holdExpiresAtUtc && s.orderStatus === 'Held' && <p className="mt-1 text-xs text-warning">Ждём оплату до {fmtDateTime(s.holdExpiresAtUtc)}</p>}
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1.5 text-right">
          <span className="inline-block rounded-full bg-cream-deep px-3 py-1 text-xs font-semibold text-ink-soft">{s.statusText}</span>
          <p className="text-sm font-semibold text-ink">{formatRub(s.totalRub)}</p>
          {s.prepayRub > 0 && <p className="text-xs text-muted">предоплата {formatRub(s.prepayRub)}</p>}
        </div>
      </Link>
    </li>
  )
}
