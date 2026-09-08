import { useState, useMemo } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, startOfMonth, endOfMonth, eachDayOfInterval, getDay, addMonths, subMonths, isToday, isPast, startOfDay } from 'date-fns'
import { ru } from 'date-fns/locale'
import { workingHoursApi, type WorkingHoursDto } from '../../api/workingHours'
import { companiesApi } from '../../api/companies'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { WeeklyTemplateModal } from '../../components/schedule/WeeklyTemplateModal'
import { getScheduleErrorMessage } from '../../utils/scheduleError'

// ── helpers ───────────────────────────────────────────────────────────────────

const toDateStr = (d: Date) => format(d, 'yyyy-MM-dd')
const toTimeStr = (t: string) => t.slice(0, 5)          // 'HH:mm:ss' → 'HH:mm'
const toApiTime = (t: string) => t.length === 5 ? t + ':00' : t  // 'HH:mm' → 'HH:mm:ss'

const WEEK_DAYS = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']

// isoWeekday: Mon=1 … Sun=7 (date-fns getDay gives Sun=0)
function isoWeekday(d: Date) {
  const d0 = getDay(d)
  return d0 === 0 ? 7 : d0
}


// ── Day editor modal ──────────────────────────────────────────────────────────

interface DayEditorProps {
  date: Date
  entry: WorkingHoursDto | undefined
  masterId: string
  companyId: string
  onClose: () => void
  onSaved: () => void
}

function DayEditor({ date, entry, masterId, companyId, onClose, onSaved }: DayEditorProps) {
  const [isWorking, setIsWorking] = useState(entry?.isWorking ?? true)
  const [start, setStart] = useState(entry ? toTimeStr(entry.startTime) : '09:00')
  const [end, setEnd]     = useState(entry ? toTimeStr(entry.endTime)   : '18:00')
  const [breaks, setBreaks] = useState<{ startTime: string; endTime: string }[]>(
    entry?.breaks.map(b => ({ startTime: toTimeStr(b.startTime), endTime: toTimeStr(b.endTime) })) ?? []
  )

  const qc = useQueryClient()
  const [mutError, setMutError] = useState('')

  const upsertMut = useMutation({
    mutationFn: () =>
      workingHoursApi.upsert({
        masterId, companyId,
        date: toDateStr(date),
        isWorking,
        startTime: toApiTime(start),
        endTime: toApiTime(end),
        breaks: breaks.map(b => ({ startTime: toApiTime(b.startTime), endTime: toApiTime(b.endTime) })),
      }),
    onSuccess: () => { setMutError(''); onSaved(); onClose() },
    onError: (err: unknown) => setMutError(getScheduleErrorMessage(err)),
  })

  const deleteMut = useMutation({
    mutationFn: () => workingHoursApi.delete(entry!.id),
    onSuccess: () => { setMutError(''); qc.invalidateQueries(); onClose() },
    onError: (err: unknown) => setMutError(getScheduleErrorMessage(err)),
  })

  const addBreak    = () => setBreaks([...breaks, { startTime: '13:00', endTime: '14:00' }])
  const removeBreak = (i: number) => setBreaks(breaks.filter((_, idx) => idx !== i))
  const updateBreak = (i: number, field: 'startTime' | 'endTime', val: string) =>
    setBreaks(breaks.map((b, idx) => idx === i ? { ...b, [field]: val } : b))

  const title = format(date, 'd MMMM yyyy', { locale: ru })

  return (
    <Modal title={title} onClose={onClose}>
      <div className="flex flex-col gap-5">
        {/* Working toggle */}
        <label className="flex items-center gap-3 cursor-pointer select-none">
          <div
            onClick={() => setIsWorking(w => !w)}
            className={`relative w-11 h-6 rounded-full transition-colors cursor-pointer ${isWorking ? 'bg-gold' : 'bg-line'}`}
          >
            <div className={`absolute top-1 w-4 h-4 bg-white rounded-full shadow transition-transform ${isWorking ? 'translate-x-6' : 'translate-x-1'}`} />
          </div>
          <span className={`text-sm font-medium ${isWorking ? 'text-ink' : 'text-muted'}`}>
            {isWorking ? 'Рабочий день' : 'Выходной'}
          </span>
        </label>

        {isWorking && (
          <>
            {/* Time range */}
            <div>
              <p className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">Рабочее время</p>
              <div className="flex items-center gap-3">
                <input
                  type="time"
                  value={start}
                  onChange={e => setStart(e.target.value)}
                  className="flex-1 rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep bg-white text-ink"
                />
                <span className="text-muted text-sm">—</span>
                <input
                  type="time"
                  value={end}
                  onChange={e => setEnd(e.target.value)}
                  className="flex-1 rounded-xl border border-line px-3.5 py-2.5 text-sm outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep bg-white text-ink"
                />
              </div>
            </div>

            {/* Breaks */}
            <div>
              <p className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">Перерывы</p>
              <div className="flex flex-col gap-2">
                {breaks.map((b, i) => (
                  <div key={i} className="flex items-center gap-2 bg-warning-bg border border-[#EAD9AC] rounded-xl px-3 py-2">
                    <span className="text-xs text-warning font-medium w-16 shrink-0">Перерыв {i + 1}</span>
                    <input
                      type="time"
                      value={b.startTime}
                      onChange={e => updateBreak(i, 'startTime', e.target.value)}
                      className="flex-1 text-sm bg-transparent outline-none text-ink"
                    />
                    <span className="text-warning text-xs">—</span>
                    <input
                      type="time"
                      value={b.endTime}
                      onChange={e => updateBreak(i, 'endTime', e.target.value)}
                      className="flex-1 text-sm bg-transparent outline-none text-ink"
                    />
                    <button onClick={() => removeBreak(i)} className="text-warning hover:text-danger ml-1">
                      <Icon name="x" size={13} strokeWidth={1.8} />
                    </button>
                  </div>
                ))}
                <button
                  onClick={addBreak}
                  className="text-sm text-muted hover:text-gold-dark border border-dashed border-line hover:border-line-strong rounded-xl px-3 py-2 transition-colors text-left"
                >
                  + Добавить перерыв
                </button>
              </div>
            </div>
          </>
        )}

        {/* Actions */}
        <div className="flex gap-3 pt-1">
          {entry && (
            <Button variant="danger" size="sm" loading={deleteMut.isPending} onClick={() => deleteMut.mutate()}>
              Удалить
            </Button>
          )}
          <Button variant="secondary" className="flex-1" onClick={onClose}>Отмена</Button>
          <Button className="flex-1" loading={upsertMut.isPending} onClick={() => upsertMut.mutate()}>
            Сохранить
          </Button>
        </div>

        {mutError && (
          <p className="text-sm text-danger text-center">{mutError}</p>
        )}
      </div>
    </Modal>
  )
}

// ── Mini calendar ─────────────────────────────────────────────────────────────

interface CalendarProps {
  month: Date
  hoursMap: Map<string, WorkingHoursDto>
  onDayClick: (d: Date) => void
}

function Calendar({ month, hoursMap, onDayClick }: CalendarProps) {
  const days = eachDayOfInterval({ start: startOfMonth(month), end: endOfMonth(month) })
  const startPad = isoWeekday(startOfMonth(month)) - 1  // Mon-based padding

  return (
    <div>
      {/* Weekday headers */}
      <div className="grid grid-cols-7 mb-1">
        {WEEK_DAYS.map(d => (
          <div key={d} className="text-center text-[11.5px] font-medium text-muted py-1">{d}</div>
        ))}
      </div>

      {/* Day cells */}
      <div className="grid grid-cols-7 gap-1">
        {/* Empty leading cells */}
        {Array.from({ length: startPad }).map((_, i) => <div key={`pad-${i}`} />)}

        {days.map(day => {
          const key = toDateStr(day)
          const entry = hoursMap.get(key)
          const past  = isPast(startOfDay(day)) && !isToday(day)
          const today = isToday(day)

          let cellClass = 'relative flex flex-col items-center justify-start pt-1.5 pb-1 rounded-[10px] h-[52px] text-sm cursor-pointer transition-all select-none border '

          if (entry?.isWorking) {
            cellClass += 'bg-cream-deep border-line-strong hover:bg-line text-ink'
          } else if (entry && !entry.isWorking) {
            cellClass += 'bg-[#F5F2EC] border-line text-muted'
          } else if (past) {
            cellClass += 'border-transparent text-line-strong hover:bg-cream-deep cursor-default'
          } else {
            cellClass += 'border-transparent hover:border-line hover:bg-cream-deep text-ink'
          }

          if (today) cellClass += ' ring-2 ring-gold-dark ring-offset-1'

          return (
            <div
              key={key}
              className={cellClass}
              onClick={() => !past && onDayClick(day)}
            >
              <span className="font-medium text-xs">
                {format(day, 'd')}
              </span>
              {entry?.isWorking && (
                <span className="text-[9px] text-gold-dark leading-tight text-center px-1">
                  {toTimeStr(entry.startTime)}–{toTimeStr(entry.endTime)}
                </span>
              )}
              {entry?.isWorking && entry.breaks.length > 0 && (
                <span className="absolute bottom-1 right-1 w-1.5 h-1.5 rounded-full bg-warning" title="Есть перерывы" />
              )}
              {entry && !entry.isWorking && (
                <span className="text-[9px] text-muted">выходной</span>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

interface Props {
  companyId: string
  /** When provided, shows only this master's own schedule without member picker */
  selfMasterId?: string
}

export function ScheduleTab({ companyId, selfMasterId }: Props) {
  const qc = useQueryClient()
  const [month, setMonth]     = useState(() => new Date())
  const [selectedDay, setSelectedDay] = useState<Date | null>(null)
  const [selectedMasterId, setSelectedMasterId] = useState<string>('')

  const { data: members } = useQuery({
    queryKey: ['company-members', companyId],
    queryFn: () => companiesApi.getMembers(companyId),
    enabled: !selfMasterId,
  })

  const masters = selfMasterId
    ? []
    : (members?.filter(m => m.role === 'Master' || m.role === 'CompanyOwner') ?? [])
        .sort((a, b) => {
          if (a.role === 'CompanyOwner' && b.role !== 'CompanyOwner') return -1
          if (a.role !== 'CompanyOwner' && b.role === 'CompanyOwner') return 1
          return a.firstName.localeCompare(b.firstName)
        })

  const masterId = selfMasterId || selectedMasterId || masters[0]?.userId || ''

  const from = toDateStr(startOfMonth(month))
  const to   = toDateStr(endOfMonth(month))

  const { data: hours, isLoading } = useQuery({
    queryKey: ['working-hours', masterId, companyId, from, to],
    queryFn: () => workingHoursApi.get(masterId, companyId, from, to),
    enabled: !!masterId,
  })

  const hoursMap = useMemo(() => {
    const map = new Map<string, WorkingHoursDto>()
    hours?.forEach(h => map.set(h.date, h))
    return map
  }, [hours])

  const onSaved = () => qc.invalidateQueries({ queryKey: ['working-hours', masterId, companyId, from, to] })

  // Declared before the early return below — a hook after a conditional `return` breaks the rule
  // that hooks run unconditionally in the same order on every render (react-hooks/rules-of-hooks);
  // members loading in vs. becoming empty between renders would otherwise skip this hook sometimes.
  const [showTemplate, setShowTemplate] = useState(false)

  if (!selfMasterId && masters.length === 0 && members !== undefined) {
    return (
      <Card className="p-12 text-center text-muted">
        <Icon name="users" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
        <p>Сначала добавьте сотрудников на вкладке «Сотрудники»</p>
      </Card>
    )
  }

  return (
    <div>
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-ink">Расписание</h2>
        {masterId && (
          <Button variant="secondary" size="sm" onClick={() => setShowTemplate(true)}>
            Шаблон
          </Button>
        )}
      </div>

      {showTemplate && masterId && (
        <WeeklyTemplateModal
          masterId={masterId}
          companyId={companyId}
          onClose={() => setShowTemplate(false)}
        />
      )}

      {/* Master avatars — shown only in owner mode */}
      {!selfMasterId && (
      <div className="flex flex-wrap gap-2 mb-4">
        {masters.map(m => {
          const active = m.userId === masterId
          return (
            <button
              key={m.userId}
              onClick={() => setSelectedMasterId(m.userId)}
              className={`flex items-center gap-2 px-3.5 py-2 rounded-full border text-sm transition-all ${
                active
                  ? 'bg-cream-deep border-line-strong text-ink'
                  : 'bg-white border-line text-ink-soft hover:border-line-strong'
              }`}
            >
              <div className={`w-6 h-6 rounded-full flex items-center justify-center font-semibold text-[10.5px] shrink-0 ${
                active ? 'bg-gold-dark text-cream' : 'bg-cream-deep text-gold-dark'
              }`}>
                {m.firstName[0]}{m.lastName[0]}
              </div>
              <span className="font-medium">{m.firstName} {m.lastName}</span>
            </button>
          )
        })}
      </div>
      )}

      <Card className="p-[22px]">
        {/* Month navigation */}
        <div className="flex items-center justify-between mb-4">
          <button
            onClick={() => setMonth(m => subMonths(m, 1))}
            className="w-[30px] h-[30px] rounded-full bg-cream-deep hover:bg-line flex items-center justify-center text-ink-soft transition-colors"
          >
            <Icon name="chevron-left" size={14} strokeWidth={1.8} />
          </button>
          <h3 className="text-[15px] font-semibold text-ink capitalize">
            {format(month, 'LLLL yyyy', { locale: ru })}
          </h3>
          <button
            onClick={() => setMonth(m => addMonths(m, 1))}
            className="w-[30px] h-[30px] rounded-full bg-cream-deep hover:bg-line flex items-center justify-center text-ink-soft transition-colors"
          >
            <Icon name="chevron-right" size={14} strokeWidth={1.8} />
          </button>
        </div>

        {isLoading ? (
          <div className="grid grid-cols-7 gap-1">
            {Array.from({ length: 35 }).map((_, i) => (
              <div key={i} className="h-[52px] bg-cream-deep rounded-[10px] animate-pulse" />
            ))}
          </div>
        ) : (
          <Calendar month={month} hoursMap={hoursMap} onDayClick={setSelectedDay} />
        )}

        {/* Legend */}
        <div className="flex items-center gap-4 mt-4 pt-4 border-t border-cream-deep">
          <div className="flex items-center gap-1.5">
            <div className="w-[11px] h-[11px] rounded-[3px] bg-cream-deep border border-line-strong" />
            <span className="text-xs text-ink-soft">Рабочий день</span>
          </div>
          <div className="flex items-center gap-1.5">
            <div className="w-[11px] h-[11px] rounded-[3px] bg-[#F5F2EC] border border-line" />
            <span className="text-xs text-ink-soft">Выходной</span>
          </div>
          <div className="flex items-center gap-1.5">
            <div className="w-1.5 h-1.5 rounded-full bg-warning" />
            <span className="text-xs text-ink-soft">Есть перерывы</span>
          </div>
        </div>

        <p className="text-xs text-muted mt-2">Кликните на дату чтобы настроить расписание</p>
      </Card>

      {/* Day editor modal */}
      {selectedDay && (
        <DayEditor
          date={selectedDay}
          entry={hoursMap.get(toDateStr(selectedDay))}
          masterId={masterId}
          companyId={companyId}
          onClose={() => setSelectedDay(null)}
          onSaved={onSaved}
        />
      )}
    </div>
  )
}
