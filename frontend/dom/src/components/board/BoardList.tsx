import { Link } from 'react-router-dom'
import type { BoardItemDto, StaysBoardDto } from '../../types'
import { BAR_CLASSES, barTone, rowsOf } from '../../utils/boardLayout'
import { formatDateShort, formatInstantInZone, nightsBetween, nightsLabel } from '../../utils/stayDates'

/**
 * The board on a phone (SPEC: «сетка/список на телефоне»): a list per house in check-in order — dates, who, the state in words, and for a
 * held booking the time the hold ends. Thumb-sized rows; a booking opens its card, a block opens the block dialog.
 */
export function BoardList({
  companyId,
  board,
  timeZoneId,
  onOpenBlock,
}: {
  companyId: string
  board: StaysBoardDto
  timeZoneId: string
  onOpenBlock: (item: BoardItemDto) => void
}) {
  const rows = rowsOf(board)
  return (
    <div className="flex flex-col gap-5" data-testid="board-list">
      {rows.map(({ house, items }) => (
        <section key={house.id} aria-labelledby={`bh-${house.id}`}>
          <h3 id={`bh-${house.id}`} className="mb-2 font-serif text-lg text-ink">
            {house.name}
            {(house.isArchived || !house.isPublished) && <span className="ml-2 text-xs font-sans font-normal text-muted">{house.isArchived ? 'в архиве' : 'не опубликован'}</span>}
          </h3>
          {items.length === 0 ? (
            <p className="rounded-2xl border border-dashed border-line px-4 py-3 text-sm text-muted">В этом периоде свободно</p>
          ) : (
            <ul className="flex flex-col gap-2">
              {items.map((item) => {
                const body = (
                  <>
                    <span className="min-w-0 flex-1">
                      <span className="block text-sm font-semibold text-ink">
                        {formatDateShort(item.startDate)} — {formatDateShort(item.endDate)}
                        <span className="ml-2 text-xs font-normal text-muted">{nightsLabel(nightsBetween(item.startDate, item.endDate))}</span>
                      </span>
                      <span className="mt-0.5 block truncate text-sm text-ink-soft">{item.label}</span>
                      {item.state === 'Held' && item.holdExpiresAtUtc && (
                        <span className="mt-0.5 block text-xs text-warning">ждём оплату до {formatInstantInZone(item.holdExpiresAtUtc, timeZoneId)}</span>
                      )}
                    </span>
                    <span className={`shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold ${BAR_CLASSES[barTone(item.state)]}`}>
                      {item.needsAction && <span aria-hidden="true">● </span>}
                      {item.stateText}
                    </span>
                  </>
                )
                const cls = 'flex min-h-[56px] items-center gap-3 rounded-2xl border border-line bg-white px-4 py-3 text-left'
                return (
                  <li key={`${item.kind}-${item.id}`}>
                    {item.kind === 'Booking' ? (
                      <Link to={`/cabinet/${companyId}/bookings/${item.id}`} className={`${cls} !text-ink`}>
                        {body}
                      </Link>
                    ) : item.kind === 'Block' ? (
                      <button type="button" className={`${cls} w-full`} onClick={() => onOpenBlock(item)}>
                        {body}
                      </button>
                    ) : (
                      <div className={cls}>{body}</div>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </section>
      ))}
    </div>
  )
}
