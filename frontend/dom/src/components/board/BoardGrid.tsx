import { Link } from 'react-router-dom'
import type { BoardItemDto, StaysBoardDto } from '../../types'
import { BAR_CLASSES, barGeometry, barTone, rowsOf } from '../../utils/boardLayout'
import { WEEKDAY_HEADERS, addDays, formatDateShort, nightsBetween, nightsLabel, weekdayMon0 } from '../../utils/stayDates'

const DAY_PX = 44
const NAME_PX = 148

/**
 * The board as a grid (tablet and up): a row per house, a column per day, a bar per stay or block. The state of a bar is in its text
 * (`stateText` on the bar and in its accessible name), the colour only supports it. A click on an empty cell starts a block there; a bar
 * opens the booking card (or the block).
 */
export function BoardGrid({
  companyId,
  board,
  from,
  days,
  today,
  canBlock,
  onBlock,
  onOpenBlock,
}: {
  companyId: string
  board: StaysBoardDto
  from: string
  days: number
  today: string
  canBlock: boolean
  onBlock: (houseId: string, firstNight: string) => void
  onOpenBlock: (item: BoardItemDto) => void
}) {
  const rows = rowsOf(board)
  const dates = Array.from({ length: days }, (_, i) => addDays(from, i))
  const width = NAME_PX + days * DAY_PX

  return (
    <div className="overflow-x-auto rounded-2xl border border-line bg-white" data-testid="board-grid">
      <div style={{ minWidth: width }}>
        <div className="flex border-b border-line bg-cream-deep/50">
          <div className="sticky left-0 z-20 shrink-0 bg-cream-deep px-3 py-2 text-xs font-semibold text-ink-soft" style={{ width: NAME_PX }}>
            Дом
          </div>
          <div className="grid flex-1" style={{ gridTemplateColumns: `repeat(${days}, minmax(${DAY_PX}px, 1fr))` }}>
            {dates.map((d) => {
              const wd = weekdayMon0(d)
              return (
                <div
                  key={d}
                  className={`border-l border-line/70 py-1.5 text-center text-[11px] leading-tight ${wd >= 5 ? 'text-gold-dark' : 'text-ink-soft'} ${d === today ? 'bg-gold/15 font-bold text-ink' : ''}`}
                >
                  <div className="uppercase">{WEEKDAY_HEADERS[wd]}</div>
                  <div className="text-sm font-medium">{Number(d.slice(8))}</div>
                  {(d === from || d.slice(8) === '01') && <div className="text-[9px] uppercase text-muted">{formatDateShort(d).split(' ')[1]}</div>}
                </div>
              )
            })}
          </div>
        </div>

        {rows.map(({ house, items }) => (
          <div key={house.id} className="flex border-b border-line/70 last:border-b-0">
            <div className="sticky left-0 z-10 flex shrink-0 flex-col justify-center bg-white px-3 py-2" style={{ width: NAME_PX }}>
              <p className="truncate text-sm font-medium text-ink" title={house.name}>
                {house.name}
              </p>
              {(house.isArchived || !house.isPublished) && <p className="text-[10px] text-muted">{house.isArchived ? 'в архиве' : 'не опубликован'}</p>}
            </div>
            <div className="relative flex-1" style={{ height: 56 }}>
              <div className="absolute inset-0 grid" style={{ gridTemplateColumns: `repeat(${days}, minmax(${DAY_PX}px, 1fr))` }} aria-hidden="true">
                {dates.map((d) => (
                  <button
                    key={d}
                    type="button"
                    tabIndex={-1}
                    disabled={!canBlock || d < today || house.isArchived}
                    onClick={() => onBlock(house.id, d)}
                    className={`border-l border-line/70 ${d === today ? 'bg-gold/10' : weekdayMon0(d) >= 5 ? 'bg-cream-deep/40' : ''} enabled:hover:bg-cream-deep/70`}
                  />
                ))}
              </div>
              {items.map((item) => {
                const g = barGeometry(item, from, days)
                if (!g) return null
                const tone = barTone(item.state)
                const range = `${formatDateShort(item.startDate)} — ${formatDateShort(item.endDate)}, ${nightsLabel(nightsBetween(item.startDate, item.endDate))}`
                const name = `${item.label}: ${item.stateText}. ${range}${item.needsAction ? '. Нужно проверить оплату' : ''}`
                const style = { left: `${(g.left / days) * 100}%`, width: `calc(${(g.width / days) * 100}% - 2px)`, top: 6, height: 44 }
                const cls = `absolute z-[5] flex flex-col justify-center overflow-hidden px-2 text-left text-[11px] leading-tight ${BAR_CLASSES[tone]} ${g.clippedLeft ? 'rounded-l-none' : 'rounded-l-xl'} ${g.clippedRight ? 'rounded-r-none' : 'rounded-r-xl'}`
                const inner = (
                  <>
                    <span className="truncate font-semibold">{item.label}</span>
                    <span className="truncate opacity-90">
                      {item.needsAction && <span aria-hidden="true">● </span>}
                      {item.stateText}
                    </span>
                  </>
                )
                if (item.kind === 'Booking') {
                  return (
                    <Link key={`${item.kind}-${item.id}`} to={`/cabinet/${companyId}/bookings/${item.id}`} className={cls} style={style} aria-label={name} title={name}>
                      {inner}
                    </Link>
                  )
                }
                if (item.kind === 'Block') {
                  return (
                    <button key={`${item.kind}-${item.id}`} type="button" onClick={() => onOpenBlock(item)} className={cls} style={style} aria-label={name} title={name}>
                      {inner}
                    </button>
                  )
                }
                return (
                  <div key={`${item.kind}-${item.id}`} className={cls} style={style} role="img" aria-label={name} title={name}>
                    {inner}
                  </div>
                )
              })}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
