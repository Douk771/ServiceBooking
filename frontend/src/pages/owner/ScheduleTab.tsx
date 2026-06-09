import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { workingHoursApi, type WorkingHoursDto } from '../../api/workingHours'
import { companiesApi, type MemberDto } from '../../api/companies'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'

const DAYS: { value: number; label: string; short: string }[] = [
  { value: 1, label: 'Понедельник', short: 'Пн' },
  { value: 2, label: 'Вторник',     short: 'Вт' },
  { value: 3, label: 'Среда',       short: 'Ср' },
  { value: 4, label: 'Четверг',     short: 'Чт' },
  { value: 5, label: 'Пятница',     short: 'Пт' },
  { value: 6, label: 'Суббота',     short: 'Сб' },
  { value: 0, label: 'Воскресенье', short: 'Вс' },
]

const DEFAULT_START = '09:00'
const DEFAULT_END   = '18:00'

function timeToStr(t: string) {
  return t.slice(0, 5)
}

interface DayRowProps {
  day: { value: number; label: string }
  entry: WorkingHoursDto | undefined
  masterId: string
  companyId: string
  onSaved: () => void
}

function DayRow({ day, entry, masterId, companyId, onSaved }: DayRowProps) {
  const [isWorking, setIsWorking] = useState(entry?.isWorking ?? false)
  const [start, setStart]         = useState(entry ? timeToStr(entry.startTime) : DEFAULT_START)
  const [end, setEnd]             = useState(entry ? timeToStr(entry.endTime)   : DEFAULT_END)
  const [breaks, setBreaks]       = useState<{ startTime: string; endTime: string }[]>(
    entry?.breaks.map(b => ({ startTime: timeToStr(b.startTime), endTime: timeToStr(b.endTime) })) ?? []
  )
  const [dirty, setDirty] = useState(false)

  const mark = () => setDirty(true)

  const upsert = useMutation({
    mutationFn: () =>
      workingHoursApi.upsert({ masterId, companyId, dayOfWeek: day.value, isWorking, startTime: start + ':00', endTime: end + ':00', breaks: breaks.map(b => ({ startTime: b.startTime + ':00', endTime: b.endTime + ':00' })) }),
    onSuccess: () => { setDirty(false); onSaved() },
  })

  const addBreak = () => { setBreaks([...breaks, { startTime: '13:00', endTime: '14:00' }]); mark() }
  const removeBreak = (i: number) => { setBreaks(breaks.filter((_, idx) => idx !== i)); mark() }
  const updateBreak = (i: number, field: 'startTime' | 'endTime', val: string) => {
    setBreaks(breaks.map((b, idx) => idx === i ? { ...b, [field]: val } : b))
    mark()
  }

  return (
    <Card className={`p-4 transition-all ${isWorking ? '' : 'opacity-60'}`}>
      <div className="flex items-center gap-4 flex-wrap">
        {/* Toggle */}
        <label className="flex items-center gap-2 cursor-pointer min-w-[130px]">
          <div
            onClick={() => { setIsWorking(!isWorking); mark() }}
            className={`relative w-10 h-5 rounded-full transition-colors cursor-pointer ${isWorking ? 'bg-primary-500' : 'bg-gray-200'}`}
          >
            <div className={`absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform ${isWorking ? 'translate-x-5' : 'translate-x-0.5'}`} />
          </div>
          <span className="font-medium text-gray-800 text-sm">{day.label}</span>
        </label>

        {/* Time range */}
        {isWorking && (
          <>
            <div className="flex items-center gap-2 text-sm">
              <input
                type="time"
                value={start}
                onChange={e => { setStart(e.target.value); mark() }}
                className="rounded-lg border border-gray-200 px-2 py-1 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100"
              />
              <span className="text-gray-400">—</span>
              <input
                type="time"
                value={end}
                onChange={e => { setEnd(e.target.value); mark() }}
                className="rounded-lg border border-gray-200 px-2 py-1 text-sm outline-none focus:border-primary-400 focus:ring-2 focus:ring-primary-100"
              />
            </div>

            {/* Breaks */}
            <div className="flex items-center gap-2 flex-wrap">
              {breaks.map((b, i) => (
                <div key={i} className="flex items-center gap-1 bg-amber-50 border border-amber-200 rounded-lg px-2 py-1">
                  <span className="text-xs text-amber-600 font-medium">Перерыв</span>
                  <input
                    type="time"
                    value={b.startTime}
                    onChange={e => updateBreak(i, 'startTime', e.target.value)}
                    className="text-xs bg-transparent outline-none w-16"
                  />
                  <span className="text-amber-400 text-xs">—</span>
                  <input
                    type="time"
                    value={b.endTime}
                    onChange={e => updateBreak(i, 'endTime', e.target.value)}
                    className="text-xs bg-transparent outline-none w-16"
                  />
                  <button onClick={() => removeBreak(i)} className="text-amber-400 hover:text-red-500 text-xs ml-1">✕</button>
                </div>
              ))}
              <button
                onClick={addBreak}
                className="text-xs text-gray-400 hover:text-primary-600 border border-dashed border-gray-200 hover:border-primary-300 rounded-lg px-2 py-1 transition-colors"
              >
                + перерыв
              </button>
            </div>
          </>
        )}

        {/* Save button */}
        <div className="ml-auto">
          {dirty ? (
            <Button size="sm" loading={upsert.isPending} onClick={() => upsert.mutate()}>
              Сохранить
            </Button>
          ) : upsert.isSuccess ? (
            <span className="text-xs text-green-600 font-medium">✓ Сохранено</span>
          ) : (
            <span className="text-xs text-gray-300">{isWorking ? 'Рабочий день' : 'Выходной'}</span>
          )}
        </div>
      </div>
    </Card>
  )
}

interface Props {
  companyId: string
}

export function ScheduleTab({ companyId }: Props) {
  const qc = useQueryClient()
  const [selectedMasterId, setSelectedMasterId] = useState<string>('')

  const { data: members } = useQuery({
    queryKey: ['company-members', companyId],
    queryFn: () => companiesApi.getMembers(companyId),
  })

  const masters = members?.filter(m => m.role === 'Master' || m.role === 'CompanyOwner') ?? []
  const masterId = selectedMasterId || masters[0]?.userId || ''

  const { data: hours, isLoading } = useQuery({
    queryKey: ['working-hours', masterId, companyId],
    queryFn: () => workingHoursApi.get(masterId, companyId),
    enabled: !!masterId,
  })

  const onSaved = () => qc.invalidateQueries({ queryKey: ['working-hours', masterId, companyId] })

  if (masters.length === 0) {
    return (
      <Card className="p-12 text-center text-gray-400">
        <p className="text-3xl mb-2">👤</p>
        <p>Сначала добавьте сотрудников на вкладке «Сотрудники»</p>
      </Card>
    )
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Расписание</h2>
        {masters.length > 1 && (
          <select
            className="rounded-xl border border-gray-200 px-3 py-2 text-sm outline-none focus:border-primary-400"
            value={masterId}
            onChange={e => setSelectedMasterId(e.target.value)}
          >
            {masters.map(m => (
              <option key={m.userId} value={m.userId}>
                {m.firstName} {m.lastName}
              </option>
            ))}
          </select>
        )}
      </div>

      {masters.length === 1 && (
        <div className="flex items-center gap-2 mb-4 bg-orange-50 rounded-xl px-4 py-2">
          <div className="w-8 h-8 rounded-full bg-primary-100 flex items-center justify-center text-primary-700 font-semibold text-sm">
            {masters[0].firstName[0]}{masters[0].lastName[0]}
          </div>
          <span className="text-sm font-medium text-gray-700">{masters[0].firstName} {masters[0].lastName}</span>
        </div>
      )}

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 7 }).map((_, i) => <div key={i} className="h-14 bg-gray-100 rounded-2xl animate-pulse" />)}
        </div>
      ) : (
        <div className="grid gap-3">
          {DAYS.map(day => (
            <DayRow
              key={day.value}
              day={day}
              entry={hours?.find(h => h.dayOfWeek === day.value)}
              masterId={masterId}
              companyId={companyId}
              onSaved={onSaved}
            />
          ))}
        </div>
      )}
    </div>
  )
}
