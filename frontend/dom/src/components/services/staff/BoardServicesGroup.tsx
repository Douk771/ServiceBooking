import { Link } from 'react-router-dom'
import type { BoardServiceCellDto, StaysBoardWithServices } from '../../../types'
import { businessDateLabel } from '../../../utils/serviceTimeFormat'
import { WEEKDAY_HEADERS, addDays, weekdayMon0 } from '../../../utils/stayDates'

const DAY_PX = 44
const NAME_PX = 148

/** Sessions per (service, business day) as the server counted them: `count`, `firstStartLabel`, `crossesMidnightLabel`, `needsAction`. */
function cellText(c: BoardServiceCellDto): string {
  return `${c.count} ${c.count === 1 ? 'сеанс' : 'сеансов'}, ${c.firstStartLabel}${c.crossesMidnightLabel ? `, ${c.crossesMidnightLabel}` : ''}${c.needsAction ? '. Нужно проверить оплату' : ''}`
}

/**
 * The «Услуги» group of the board (US-39-14, API_CONTRACT_CYCLE39.md §39.30.1): a row per service and a cell per business day with
 * sessions; a cell opens «День услуг» of that day. A grid on a tablet and up (same columns as the houses above), a list on a phone.
 */
export function BoardServicesGroup({ board, companyId, from, days, today, wide }: { board: StaysBoardWithServices; companyId: string; from: string; days: number; today: string; wide: boolean }) {
  const services = board.services ?? []
  const cells = board.serviceCells ?? []
  if (services.length === 0) return null
  const dates = Array.from({ length: days }, (_, i) => addDays(from, i))
  const dayLink = (d: string) => `/cabinet/${companyId}/service-day/${d}`

  if (!wide) {
    return (
      <section className="mt-8" aria-labelledby="board-services-h" data-testid="board-services-list">
        <h3 id="board-services-h" className="mb-2 font-serif text-lg text-ink">
          Услуги
        </h3>
        <div className="flex flex-col gap-4">
          {services.map((s) => {
            const mine = cells.filter((c) => c.serviceId === s.id).sort((a, b) => a.businessDate.localeCompare(b.businessDate))
            return (
              <div key={s.id}>
                <p className="mb-1 text-sm font-semibold text-ink">
                  {s.name}
                  {(s.isArchived || !s.isPublished) && <span className="ml-2 text-xs font-normal text-muted">{s.isArchived ? 'в архиве' : 'не опубликована'}</span>}
                </p>
                {mine.length === 0 ? (
                  <p className="rounded-2xl border border-dashed border-line px-4 py-3 text-sm text-muted">В этом периоде сеансов нет</p>
                ) : (
                  <ul className="flex flex-col gap-2">
                    {mine.map((c) => (
                      <li key={c.businessDate}>
                        <Link to={dayLink(c.businessDate)} className="flex min-h-[56px] items-center justify-between gap-3 rounded-2xl border border-line bg-white px-4 py-3 !text-ink">
                          <span className="text-sm font-semibold">{businessDateLabel(c.businessDate)}</span>
                          <span className="text-sm text-ink-soft">
                            {c.needsAction && <span aria-hidden="true">● </span>}
                            {cellText(c)}
                          </span>
                        </Link>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )
          })}
        </div>
      </section>
    )
  }

  return (
    <section className="mt-6" aria-labelledby="board-services-h" data-testid="board-services-grid">
      <h3 id="board-services-h" className="mb-2 font-serif text-lg text-ink">
        Услуги
      </h3>
      <div className="overflow-x-auto rounded-2xl border border-line bg-white">
        <div style={{ minWidth: NAME_PX + days * DAY_PX }}>
          {services.map((s) => (
            <div key={s.id} className="flex border-b border-line/70 last:border-b-0">
              <div className="sticky left-0 z-10 flex shrink-0 flex-col justify-center bg-white px-3 py-2" style={{ width: NAME_PX }}>
                <p className="truncate text-sm font-medium text-ink" title={s.name}>
                  {s.name}
                </p>
                {(s.isArchived || !s.isPublished) && <p className="text-[10px] text-muted">{s.isArchived ? 'в архиве' : 'не опубликована'}</p>}
              </div>
              <div className="grid flex-1" style={{ gridTemplateColumns: `repeat(${days}, minmax(${DAY_PX}px, 1fr))`, height: 56 }}>
                {dates.map((d) => {
                  const c = cells.find((x) => x.serviceId === s.id && x.businessDate === d)
                  const base = `border-l border-line/70 ${d === today ? 'bg-gold/10' : weekdayMon0(d) >= 5 ? 'bg-cream-deep/40' : ''}`
                  if (!c) return <span key={d} className={base} aria-hidden="true" />
                  return (
                    <Link
                      key={d}
                      to={dayLink(d)}
                      className={`${base} flex flex-col items-center justify-center text-[10px] leading-tight !text-ink hover:bg-cream-deep/70 ${c.needsAction ? '!bg-gold !text-white' : ''}`}
                      aria-label={`${s.name}, ${businessDateLabel(d)}: ${cellText(c)}`}
                      title={cellText(c)}
                    >
                      <span className="text-sm font-bold tabular-nums">
                        {c.needsAction && <span aria-hidden="true">● </span>}
                        {c.count}
                      </span>
                      <span className="max-w-full truncate px-0.5">{c.firstStartLabel}</span>
                    </Link>
                  )
                })}
              </div>
            </div>
          ))}
          <div className="flex border-t border-line bg-cream-deep/40 text-[10px] text-muted">
            <div className="shrink-0 px-3 py-1" style={{ width: NAME_PX }}>
              Число сеансов
            </div>
            <div className="grid flex-1" style={{ gridTemplateColumns: `repeat(${days}, minmax(${DAY_PX}px, 1fr))` }} aria-hidden="true">
              {dates.map((d) => (
                <span key={d} className="border-l border-line/70 py-1 text-center">
                  {WEEKDAY_HEADERS[weekdayMon0(d)]}
                </span>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
