import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { useMediaQuery } from '../../hooks/useMediaQuery'
import { staysBoardApi } from '../../api/staysBoard'
import { BlockDialog, type BlockDialogTarget } from '../../components/board/BlockDialog'
import { BoardGrid } from '../../components/board/BoardGrid'
import { BoardList } from '../../components/board/BoardList'
import { ManualBookingDialog } from '../../components/board/ManualBookingDialog'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { BoardItemDto, StaysBoardDto } from '../../types'
import { BOARD_POLL_MS, clampDays, mergeBoard, shiftWindow } from '../../utils/boardLayout'
import { can } from '../../utils/permissions'
import { addDays, formatDateShort, instantToZoned } from '../../utils/stayDates'
import { getStayErrorMessage } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'

const WINDOWS = [
  { days: 14, label: '2 недели' },
  { days: 30, label: '30 дней' },
  { days: 62, label: '2 месяца' },
]

/**
 * `/cabinet/:companyId/board` (`ViewBookings`, US-37-14…16) — the board: houses × days, polled every 15 s with the revision (an unchanged
 * answer carries no arrays), the counter of «ожидают проверки оплаты», blocks and (P1) a manual booking. A grid on a tablet and up, a list
 * on a phone. Expired holds and final bookings never appear — the server leaves them out.
 */
export function BoardPage() {
  const { company, refresh } = useStaysCompany()
  const qc = useQueryClient()
  const wide = useMediaQuery('(min-width: 768px)')
  const canBlock = can(company.myPermissions, 'ManageBlocks')
  const canBook = can(company.myPermissions, 'ManageBookings')
  const viewAllowed = can(company.myPermissions, 'ViewBookings')

  const todayLocal = instantToZoned(Date.now(), company.timeZoneId).date
  const [from, setFrom] = useState(() => addDays(todayLocal, -1))
  const [daysChoice, setDaysChoice] = useState(30)
  const days = clampDays(wide ? daysChoice : Math.min(daysChoice, 30))
  const [blockTarget, setBlockTarget] = useState<BlockDialogTarget | null>(null)
  const [manual, setManual] = useState<{ houseId?: string; checkIn?: string } | null>(null)

  const key = useMemo(() => ['stays-board', company.id, from, days] as const, [company.id, from, days])
  const query = useQuery({
    queryKey: key,
    queryFn: async () => {
      const prev = qc.getQueryData<StaysBoardDto>(key)
      const next = await staysBoardApi.board(company.id, { from, days, ...(prev?.items ? { sinceRevision: prev.revision } : {}) })
      return mergeBoard(prev, next)
    },
    enabled: viewAllowed,
    staleTime: 0,
    refetchInterval: BOARD_POLL_MS,
    placeholderData: (prev) => prev,
    retry: 1,
  })
  const board = query.data
  const today = board?.today ?? todayLocal

  // The «ожидают проверки» counter lives in the cabinet header too; keep it in step with what the board has just learned.
  useEffect(() => {
    if (board && board.awaitingPaymentCount != null && board.awaitingPaymentCount !== (company.awaitingPaymentCount ?? 0)) refresh()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reacts to the board's own counter only
  }, [board?.awaitingPaymentCount])

  if (!viewAllowed) return <NotFoundPage title="Раздел недоступен" />

  const reload = () => void qc.invalidateQueries({ queryKey: ['stays-board', company.id] })
  const openBlock = (item: BoardItemDto) => {
    if (!canBlock || item.kind !== 'Block') return
    setBlockTarget({ block: { id: item.id, houseId: item.houseId, startDate: item.startDate, endDate: item.endDate, kind: item.blockKind ?? 'Other' } })
  }
  const houses = board?.houses ?? []
  const awaiting = board?.awaitingPaymentCount ?? company.awaitingPaymentCount ?? 0

  return (
    <main className="mx-auto max-w-[1280px] px-4 pb-8 pt-6 sm:px-8">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <button type="button" aria-label="Неделей раньше" onClick={() => setFrom(shiftWindow(from, -1))} className="flex h-11 w-11 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong">
            <Icon name="chevron-left" size={16} strokeWidth={1.8} />
          </button>
          <Button variant="secondary" className="min-h-[44px]" onClick={() => setFrom(addDays(today, -1))}>
            Сегодня
          </Button>
          <button type="button" aria-label="Неделей позже" onClick={() => setFrom(shiftWindow(from, 1))} className="flex h-11 w-11 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong">
            <Icon name="chevron-right" size={16} strokeWidth={1.8} />
          </button>
          <span className="text-sm text-ink-soft">
            с {formatDateShort(from, 0)}
          </span>
          <label className="sr-only" htmlFor="board-days">
            Период
          </label>
          <select id="board-days" value={days} onChange={(e) => setDaysChoice(Number(e.target.value))} className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink">
            {WINDOWS.filter((w) => wide || w.days <= 30).map((w) => (
              <option key={w.days} value={w.days}>
                {w.label}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-wrap gap-2">
          {canBook && houses.some((h) => !h.isArchived) && (
            <Button className="min-h-[44px]" onClick={() => setManual({})}>
              <Icon name="plus" size={15} /> Новая бронь
            </Button>
          )}
          {canBlock && houses.some((h) => !h.isArchived) && (
            <Button variant="secondary" className="min-h-[44px]" onClick={() => setBlockTarget({ block: null })}>
              Заблокировать даты
            </Button>
          )}
        </div>
      </div>

      {awaiting > 0 && (
        <Link
          to={`/cabinet/${company.id}/bookings`}
          className="mb-4 flex min-h-[44px] items-center justify-between gap-3 rounded-2xl bg-gold px-5 py-3 text-sm font-semibold !text-white hover:bg-gold-dark"
          data-testid="awaiting-banner"
        >
          <span>Ожидают проверки оплаты: {awaiting}</span>
          <span className="inline-flex items-center gap-1">
            Проверить <Icon name="arrow-right" size={15} strokeWidth={1.8} />
          </span>
        </Link>
      )}

      {query.isLoading ? (
        <LoadingList rows={4} rowClass="h-14" />
      ) : query.isError && !board ? (
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить шахматку.')} onRetry={() => void query.refetch()} />
      ) : !board || houses.length === 0 ? (
        <EmptyState
          title="Домов пока нет"
          text="Шахматка покажет брони и блокировки, когда у компании появятся дома."
          action={
            can(company.myPermissions, 'ManageHouses') ? (
              <Link to={`/cabinet/${company.id}/houses/new`} className="inline-flex min-h-[44px] items-center rounded-full bg-ink px-5 text-sm font-semibold !text-cream">
                Добавить дом
              </Link>
            ) : undefined
          }
        />
      ) : (
        <>
          {query.isError && (
            <p role="status" className="mb-3 rounded-xl bg-warning-bg px-4 py-2 text-xs text-warning" data-testid="board-stale">
              Не удаётся обновить данные — показано последнее известное состояние. Попробуем ещё раз через несколько секунд.
            </p>
          )}
          {wide ? (
            <BoardGrid
              companyId={company.id}
              board={board}
              from={from}
              days={days}
              today={today}
              canBlock={canBlock}
              onBlock={(houseId, firstNight) => setBlockTarget({ block: null, houseId, firstNight })}
              onOpenBlock={openBlock}
            />
          ) : (
            <BoardList companyId={company.id} board={board} timeZoneId={company.timeZoneId} onOpenBlock={openBlock} />
          )}
          <p className="mt-3 text-xs text-muted">
            Брони, ожидающие проверки оплаты, выделены; удержания без оплаты исчезают сами, когда истекает время на оплату.
          </p>
        </>
      )}

      {blockTarget && (
        <BlockDialog companyId={company.id} houses={houses} target={blockTarget} today={today} onClose={() => setBlockTarget(null)} onChanged={reload} />
      )}
      {manual && (
        <ManualBookingDialog
          companyId={company.id}
          houses={houses}
          initialHouseId={manual.houseId}
          initialCheckIn={manual.checkIn}
          onClose={() => setManual(null)}
          onCreated={() => {
            reload()
            refresh()
          }}
        />
      )}
    </main>
  )
}
