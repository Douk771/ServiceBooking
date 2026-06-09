import { useState, useMemo } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, startOfMonth, endOfMonth, eachDayOfInterval, getDay, addMonths, subMonths, isToday, isPast, startOfDay } from 'date-fns'
import { ru } from 'date-fns/locale'
import { workingHoursApi, type WorkingHoursDto } from '../../api/workingHours'
import { companiesApi } from '../../api/companies'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Modal } from '../../components/ui/Modal'

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
    onSuccess: () => { onSaved(); onClose() },
  })

  const deleteMut = useMutation({
    mutationFn: () => workingHoursApi.delete(entry!.id),
    onSuccess: () => { qc.invalidateQueries(); onClose() },
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
            className={`relative w-11 h-6 rounded-full transition-colors cursor-pointer ${isWorking ? 'bg-primary-500' : 'bg-gray-200'}`}
          >
            <div className={`absolute top-1 w-4 h-4 bg-white rounded-full shadow transition-transform ${isWorking ? 'translate-x-6' : 'translate-x-1'}`} />
          </div>
          <span className={`text-sm font-medium ${isWorking ? 'text-gray-900' : 'text-gray-400'}`}>
            {isWorking ? 'Рабочий день' : 'Выходной'}
          </span>
        </label>

        {isWorking && (
          <>
            {/* Time range */}
            <div>
              <p className="text-xs font-medium text-gray-500 mb-2 uppercase tracking-wide">Рабочее время</p>
              <div className="flex items-center gap-3">
                <input
                  type="time"
                  value={start}
                  onChange={e => setStart(e.target.value)}
                  className="flex-1 rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100"
                />
                <span className="text-gray-400 text-sm">—</span>
                <input
                  type="time"
                  value={end}
                  onChange={e => setEnd(e.target.value)}
                  className="flex-1 rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100"
                />
              </div>
            </div>

            {/* Breaks */}
            <div>
              <p className="text-xs font-medium text-gray-500 mb-2 uppercase tracking-wide">Перерывы</p>
              <div className="flex flex-col gap-2">
                {breaks.map((b, i) => (
                  <div key={i} className="flex items-center gap-2 bg-amber-50 border border-amber-200 rounded-xl px-3 py-2">
                    <span className="text-xs text-amber-700 font-medium w-16 shrink-0">Перерыв {i + 1}</span>
                    <input
                      type="time"
                      value={b.startTime}
                      onChange={e => updateBreak(i, 'startTime', e.target.value)}
                      className="flex-1 text-sm bg-transparent outline-none"
                    />
                    <span className="text-amber-400 text-xs">—</span>
                    <input
                      type="time"
                      value={b.endTime}
                      onChange={e => updateBreak(i, 'endTime', e.target.value)}
                      className="flex-1 text-sm bg-transparent outline-none"
                    />
                    <button onClick={() => removeBreak(i)} className="text-amber-400 hover:text-red-500 text-sm ml-1">✕</button>
                  </div>
                ))}
                <button
                  onClick={addBreak}
                  className="text-sm text-gray-400 hover:text-primary-600 border border-dashed border-gray-200 hover:border-primary-300 rounded-xl px-3 py-2 transition-colors text-left"
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

        {upsertMut.isError && (
          <p className="text-sm text-red-500 text-center">Ошибка сохранения</p>
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
          <div key={d} className="text-center text-xs font-medium text-gray-400 py-1">{d}</div>
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

          let cellClass = 'relative flex flex-col items-center justify-start pt-1 pb-1 rounded-xl h-14 text-sm cursor-pointer transition-all select-none border '

          if (entry?.isWorking) {
            cellClass += 'bg-primary-50 border-primary-200 hover:bg-primary-100 text-primary-800'
          } else if (entry && !entry.isWorking) {
            cellClass += 'bg-gray-50 border-gray-200 hover:bg-gray-100 text-gray-400'
          } else if (past) {
            cellClass += 'border-transparent text-gray-300 hover:bg-gray-50 cursor-default'
          } else {
            cellClass += 'border-transparent hover:border-gray-200 hover:bg-gray-50 text-gray-700'
          }

          if (today) cellClass += ' ring-2 ring-primary-400 ring-offset-1'

          return (
            <div
              key={key}
              className={cellClass}
              onClick={() => !past && onDayClick(day)}
            >
              <span className={`font-medium text-xs ${today ? 'text-primary-600' : ''}`}>
                {format(day, 'd')}
              </span>
              {entry?.isWorking && (
                <span className="text-[10px] text-primary-500 leading-tight text-center px-1">
                  {toTimeStr(entry.startTime)}–{toTimeStr(entry.endTime)}
                </span>
              )}
              {entry?.isWorking && entry.breaks.length > 0 && (
                <span className="absolute bottom-1 right-1 w-1.5 h-1.5 rounded-full bg-amber-400" title="Есть перерывы" />
              )}
              {entry && !entry.isWorking && (
                <span className="text-[10px] text-gray-400">выходной</span>
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
}

export function ScheduleTab({ companyId }: Props) {
  const qc = useQueryClient()
  const [month, setMonth]     = useState(() => new Date())
  const [selectedDay, setSelectedDay] = useState<Date | null>(null)
  const [selectedMasterId, setSelectedMasterId] = useState<string>('')

  const { data: members } = useQuery({
    queryKey: ['company-members', companyId],
    queryFn: () => companiesApi.getMembers(companyId),
  })

  const masters = members?.filter(m => m.role === 'Master' || m.role === 'CompanyOwner') ?? []
  const masterId = selectedMasterId || masters[0]?.userId || ''

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

  if (masters.length === 0) {
    return (
      <Card className="p-12 text-center text-gray-400">
        <p className="text-3xl mb-2">👤</p>
        <p>Сначала добавьте сотрудников на вкладке «Сотрудники»</p>
      </Card>
    )
  }

  const selectedMaster = masters.find(m => m.userId === masterId) ?? masters[0]

  return (
    <div>
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Расписание</h2>
      </div>

      {/* Master avatars — always shown, click to select */}
      <div className="flex flex-wrap gap-2 mb-4">
        {masters.map(m => {
          const active = m.userId === masterId
          return (
            <button
              key={m.userId}
              onClick={() => setSelectedMasterId(m.userId)}
              className={`flex items-center gap-2 px-3 py-2 rounded-xl border text-sm transition-all ${
                active
                  ? 'bg-orange-50 border-primary-300 text-gray-900 shadow-sm'
                  : 'bg-white border-gray-200 text-gray-500 hover:border-gray-300 hover:text-gray-700'
              }`}
            >
              <div className={`w-7 h-7 rounded-full flex items-center justify-center font-semibold text-xs shrink-0 ${
                active ? 'bg-primary-500 text-white' : 'bg-primary-100 text-primary-700'
              }`}>
                {m.firstName[0]}{m.lastName[0]}
              </div>
              <span className="font-medium">{m.firstName} {m.lastName}</span>
            </button>
          )
        })}
      </div>

      <Card className="p-5">
        {/* Month navigation */}
        <div className="flex items-center justify-between mb-4">
          <button
            onClick={() => setMonth(m => subMonths(m, 1))}
            className="w-8 h-8 rounded-full hover:bg-gray-100 flex items-center justify-center text-gray-500 text-lg transition-colors"
          >
            ‹
          </button>
          <h3 className="text-base font-semibold text-gray-900 capitalize">
            {format(month, 'LLLL yyyy', { locale: ru })}
          </h3>
          <button
            onClick={() => setMonth(m => addMonths(m, 1))}
            className="w-8 h-8 rounded-full hover:bg-gray-100 flex items-center justify-center text-gray-500 text-lg transition-colors"
          >
            ›
          </button>
        </div>

        {isLoading ? (
          <div className="grid grid-cols-7 gap-1">
            {Array.from({ length: 35 }).map((_, i) => (
              <div key={i} className="h-14 bg-gray-100 rounded-xl animate-pulse" />
            ))}
          </div>
        ) : (
          <Calendar month={month} hoursMap={hoursMap} onDayClick={setSelectedDay} />
        )}

        {/* Legend */}
        <div className="flex items-center gap-4 mt-4 pt-4 border-t border-gray-100">
          <div className="flex items-center gap-1.5">
            <div className="w-3 h-3 rounded bg-primary-100 border border-primary-200" />
            <span className="text-xs text-gray-500">Рабочий день</span>
          </div>
          <div className="flex items-center gap-1.5">
            <div className="w-3 h-3 rounded bg-gray-100 border border-gray-200" />
            <span className="text-xs text-gray-500">Выходной</span>
          </div>
          <div className="flex items-center gap-1.5">
            <div className="w-1.5 h-1.5 rounded-full bg-amber-400" />
            <span className="text-xs text-gray-500">Есть перерывы</span>
          </div>
        </div>

        <p className="text-xs text-gray-400 mt-2">Кликните на дату чтобы настроить расписание</p>
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
