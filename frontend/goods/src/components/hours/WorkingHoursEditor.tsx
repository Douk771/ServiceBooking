import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { scheduleApi } from '../../api/schedule'
import { ErrorState, InlineError, LoadingList } from '../StatePanels'
import { applyDayTo, crossesMidnight, emptyWeek, firstWeekError, MAX_INTERVALS_PER_DAY, DEFAULT_INTERVAL, weekFromDto, weekToInput, type EditableWeek } from '../../utils/hours'
import { getGoodsErrorMessage } from '../../utils/orderError'
import { WEEKDAYS } from '../../utils/weekdays'
import type { DayOfWeek } from '../../types'

const TIME_INPUT = 'rounded-xl border border-line bg-white px-3 py-2 text-sm text-ink outline-none focus:border-gold min-h-[44px]'
const WORKDAYS: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']

/**
 * US-24-01 — the weekly schedule: 0–3 intervals per day, 5-minute step, «через полночь» hint. The form only guards field
 * format; overlap and midnight rules are the server's and its 400 text is printed as is (API_CONTRACT_CYCLE24.md §473.2).
 */
export function WorkingHoursEditor({ shopId, onSaved }: { shopId: string; onSaved: () => void }) {
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['working-hours', shopId], queryFn: () => scheduleApi.workingHours(shopId) })
  const [week, setWeek] = useState<EditableWeek>(emptyWeek)
  const [dirty, setDirty] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (q.data && !dirty) setWeek(weekFromDto(q.data.days))
  }, [q.data, dirty])

  const save = useMutation({
    mutationFn: () => scheduleApi.putWorkingHours(shopId, weekToInput(week)),
    onSuccess: (dto) => {
      qc.setQueryData(['working-hours', shopId], dto)
      setWeek(weekFromDto(dto.days))
      setDirty(false)
      setSaved(true)
      onSaved()
    },
  })

  const edit = (next: EditableWeek) => {
    setWeek(next)
    setDirty(true)
    setSaved(false)
    setLocalError(null)
    save.reset()
  }
  const setInterval = (day: DayOfWeek, idx: number, patch: Partial<{ start: string; end: string }>) =>
    edit({ ...week, [day]: week[day].map((i, k) => (k === idx ? { ...i, ...patch } : i)) })
  const addInterval = (day: DayOfWeek) =>
    edit({ ...week, [day]: [...week[day], week[day].length === 0 ? { ...DEFAULT_INTERVAL } : { start: '', end: '' }] })
  const removeInterval = (day: DayOfWeek, idx: number) => edit({ ...week, [day]: week[day].filter((_, k) => k !== idx) })

  const submit = () => {
    const problem = firstWeekError(week)
    if (problem) return setLocalError(problem)
    save.mutate()
  }

  if (q.isLoading) return <LoadingList rows={4} rowClass="h-16" />
  if (q.isError) return <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить часы работы.')} onRetry={() => void q.refetch()} />

  return (
    <section aria-labelledby="hours-title" className="rounded-2xl border border-line bg-white p-5 sm:p-6">
      <h2 id="hours-title" className="font-serif text-xl text-ink">
        Часы работы
      </h2>
      <p className="text-sm text-ink-soft mt-1">
        В эти часы покупатели выбирают время получения. Без часов магазин не принимает заказы. Если магазин работает после полуночи, укажите
        конец раньше начала — например, 18:00–03:00.
      </p>

      <ul className="mt-4 flex flex-col divide-y divide-line">
        {WEEKDAYS.map((d) => (
          <li key={d.value} className="py-3 flex flex-col sm:flex-row sm:items-start gap-2 sm:gap-4">
            <p className="sm:w-32 pt-2.5 text-sm font-semibold text-ink shrink-0">{d.full}</p>
            <div className="flex-1 flex flex-col gap-2">
              {week[d.value].length === 0 && <p className="text-sm text-muted pt-2.5">Выходной</p>}
              {week[d.value].map((i, idx) => (
                <div key={idx} className="flex items-center gap-2 flex-wrap">
                  <label className="sr-only" htmlFor={`${d.value}-s-${idx}`}>{`${d.full}, интервал ${idx + 1}: с`}</label>
                  <input id={`${d.value}-s-${idx}`} type="time" step={300} className={TIME_INPUT} value={i.start} onChange={(e) => setInterval(d.value, idx, { start: e.target.value })} />
                  <span aria-hidden="true" className="text-muted">
                    –
                  </span>
                  <label className="sr-only" htmlFor={`${d.value}-e-${idx}`}>{`${d.full}, интервал ${idx + 1}: до`}</label>
                  <input id={`${d.value}-e-${idx}`} type="time" step={300} className={TIME_INPUT} value={i.end} onChange={(e) => setInterval(d.value, idx, { end: e.target.value })} />
                  {crossesMidnight(i) && <span className="text-xs text-ink-soft">до утра</span>}
                  <button type="button" onClick={() => removeInterval(d.value, idx)} aria-label={`Убрать интервал ${idx + 1}: ${d.full}`} className="w-11 h-11 rounded-full flex items-center justify-center text-muted hover:text-danger hover:bg-cream-deep">
                    <Icon name="trash" size={15} strokeWidth={1.8} />
                  </button>
                </div>
              ))}
              <div className="flex gap-2 flex-wrap">
                {week[d.value].length < MAX_INTERVALS_PER_DAY && (
                  <Button variant="ghost" size="sm" className="min-h-[44px]" onClick={() => addInterval(d.value)}>
                    <Icon name="plus" size={13} /> {week[d.value].length === 0 ? 'Задать часы' : 'Ещё интервал (перерыв)'}
                  </Button>
                )}
                {week[d.value].length > 0 && d.value === 'Monday' && (
                  <Button variant="ghost" size="sm" className="min-h-[44px]" onClick={() => edit(applyDayTo(week, 'Monday', WORKDAYS))}>
                    Применить ко всем будням
                  </Button>
                )}
              </div>
            </div>
          </li>
        ))}
      </ul>

      {(localError || save.isError) && (
        <div className="mt-3">
          <InlineError>{localError ?? getGoodsErrorMessage(save.error, 'Не удалось сохранить часы работы.')}</InlineError>
        </div>
      )}
      <div className="mt-4 flex items-center gap-3 flex-wrap">
        <Button onClick={submit} loading={save.isPending} disabled={!dirty}>
          Сохранить часы
        </Button>
        {saved && (
          <span role="status" className="text-sm text-success font-medium">
            Сохранено. Изменения действуют на новые заказы.
          </span>
        )}
      </div>
    </section>
  )
}
