import { Link } from 'react-router-dom'
import type { ServiceDayDto, ServiceDayServiceDto, ServiceDayBarDto } from '../../../types'
import { axisTicks, barBox, barTimeText, sortedBars } from '../../../utils/serviceDay'

const BAR_CLASS: Record<ServiceDayBarDto['kind'], string> = {
  Session: 'border border-success/40 bg-success-bg text-success',
  Buffer: 'border border-dashed border-line-strong bg-cream-deep text-ink-soft',
  CarryOverBuffer: 'border border-dashed border-line-strong bg-cream-deep text-ink-soft',
}
const HELD_CLASS = 'border border-dashed border-warning bg-warning-bg text-warning'
const NEEDS_ACTION_CLASS = 'border border-gold-dark bg-gold text-white shadow-soft'

const barClass = (b: ServiceDayBarDto) => (b.kind !== 'Session' ? BAR_CLASS[b.kind] : b.needsAction ? NEEDS_ACTION_CLASS : b.state === 'Held' ? HELD_CLASS : BAR_CLASS.Session)

const barName = (b: ServiceDayBarDto) => `${b.label}${b.stateText ? `: ${b.stateText}` : ''}. ${barTimeText(b)}${b.needsAction ? '. Нужно проверить оплату' : ''}`

/** The scale (tablet and up): a row per service, the axis in hours with the midnight marked, the windows as a light band, a bar per session or buffer. */
export function ServiceDayScale({ day, companyId }: { day: ServiceDayDto; companyId: string }) {
  const ticks = axisTicks(day.axis, day.axis.toMinute - day.axis.fromMinute > 600 ? 2 : 1)
  return (
    <div className="overflow-x-auto rounded-2xl border border-line bg-white" data-testid="service-day-scale">
      <div style={{ minWidth: 720 }}>
        <div className="flex border-b border-line bg-cream-deep/50">
          <div className="w-[148px] shrink-0 px-3 py-2 text-xs font-semibold text-ink-soft">Услуга</div>
          <div className="relative h-8 flex-1" aria-hidden="true">
            {ticks.map((t) => (
              <span key={t.minute} className={`absolute top-1.5 -translate-x-1/2 text-[11px] ${t.isMidnight ? 'font-bold text-ink' : 'text-ink-soft'}`} style={{ left: `${t.leftPct}%` }}>
                {t.label}
              </span>
            ))}
          </div>
        </div>
        {day.services.map((s) => (
          <ServiceRow key={s.id} s={s} day={day} companyId={companyId} />
        ))}
      </div>
    </div>
  )
}

function ServiceRow({ s, day, companyId }: { s: ServiceDayServiceDto; day: ServiceDayDto; companyId: string }) {
  const ticks = axisTicks(day.axis)
  return (
    <div className="flex border-b border-line/70 last:border-b-0">
      <div className="flex w-[148px] shrink-0 flex-col justify-center px-3 py-2">
        <p className="truncate text-sm font-medium text-ink" title={s.name}>
          {s.name}
        </p>
        {!s.isPublished && <p className="text-[10px] text-muted">не опубликована</p>}
        {s.closed && <p className="text-[10px] text-muted">закрыто</p>}
      </div>
      <div className="relative flex-1" style={{ height: 56 }}>
        {ticks.map((t) => (
          <span key={t.minute} className={`absolute inset-y-0 border-l ${t.isMidnight ? 'border-ink/50' : 'border-line/60'}`} style={{ left: `${t.leftPct}%` }} aria-hidden="true" />
        ))}
        {s.windows.map((w, i) => {
          const g = barBox(day.axis, w.startMinute, w.endMinute)
          return <span key={i} className="absolute inset-y-0 bg-cream-deep/60" style={{ left: `${g.leftPct}%`, width: `${g.widthPct}%` }} title={`Свободное время: ${w.label}`} aria-hidden="true" />
        })}
        {sortedBars(s.bars).map((b, i) => {
          const g = barBox(day.axis, b.startMinute, b.endMinute)
          const cls = `absolute z-[5] flex flex-col justify-center overflow-hidden px-2 text-left text-[11px] leading-tight ${barClass(b)} ${g.clippedLeft ? 'rounded-l-none' : 'rounded-l-xl'} ${g.clippedRight ? 'rounded-r-none' : 'rounded-r-xl'}`
          const style = { left: `${g.leftPct}%`, width: `calc(${g.widthPct}% - 1px)`, top: 6, height: 44 }
          const inner = (
            <>
              <span className="truncate font-semibold">{b.label}</span>
              <span className="truncate opacity-90">
                {b.needsAction && <span aria-hidden="true">● </span>}
                {b.stateText ?? barTimeText(b)}
              </span>
            </>
          )
          return b.kind === 'Session' && b.sessionId ? (
            <Link key={`${b.kind}-${b.sessionId}-${i}`} to={`/cabinet/${companyId}/service-sessions/${b.sessionId}`} className={cls} style={style} aria-label={barName(b)} title={barName(b)}>
              {inner}
            </Link>
          ) : (
            <div key={`${b.kind}-${i}`} className={cls} style={style} role="img" aria-label={barName(b)} title={barName(b)}>
              {inner}
            </div>
          )
        })}
      </div>
    </div>
  )
}

/** The list (phone): per service the windows in words and the sessions in time order, thumb-sized rows. */
export function ServiceDayList({ day, companyId }: { day: ServiceDayDto; companyId: string }) {
  return (
    <div className="flex flex-col gap-5" data-testid="service-day-list">
      {day.services.map((s) => {
        const bars = sortedBars(s.bars)
        return (
          <section key={s.id} aria-labelledby={`sd-${s.id}`}>
            <h3 id={`sd-${s.id}`} className="font-serif text-lg text-ink">
              {s.name}
              {!s.isPublished && <span className="ml-2 font-sans text-xs font-normal text-muted">не опубликована</span>}
            </h3>
            <p className="mb-2 text-xs text-ink-soft">{s.closed || s.windows.length === 0 ? 'В этот день закрыто' : `Свободное время: ${s.windows.map((w) => w.label).join('; ')}`}</p>
            {bars.length === 0 ? (
              <p className="rounded-2xl border border-dashed border-line px-4 py-3 text-sm text-muted">Сеансов нет</p>
            ) : (
              <ul className="flex flex-col gap-2">
                {bars.map((b, i) => {
                  const body = (
                    <>
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-semibold text-ink">{barTimeText(b)}</span>
                        <span className="block truncate text-sm text-ink-soft">{b.label}</span>
                      </span>
                      <span className={`shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold ${barClass(b)}`}>
                        {b.needsAction && <span aria-hidden="true">● </span>}
                        {b.stateText ?? (b.kind === 'Session' ? '' : 'подготовка')}
                      </span>
                    </>
                  )
                  const cls = 'flex min-h-[56px] items-center gap-3 rounded-2xl border border-line bg-white px-4 py-3 text-left'
                  return (
                    <li key={`${b.kind}-${i}`}>
                      {b.kind === 'Session' && b.sessionId ? (
                        <Link to={`/cabinet/${companyId}/service-sessions/${b.sessionId}`} className={`${cls} !text-ink`}>
                          {body}
                        </Link>
                      ) : (
                        <div className={`${cls} opacity-80`}>{body}</div>
                      )}
                    </li>
                  )
                })}
              </ul>
            )}
          </section>
        )
      })}
    </div>
  )
}
