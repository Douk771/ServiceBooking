import { useState, useEffect } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, startOfMonth, endOfMonth, addDays } from 'date-fns'
import { scheduleTemplateApi, type DayTemplate } from '../../api/scheduleTemplate'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'

const DAY_NAMES = ['', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']

const DEFAULT_DAYS: DayTemplate[] = [1, 2, 3, 4, 5, 6, 7].map(d => ({
  dayOfWeek: d,
  isWorking: d <= 5,
  startTime: '09:00',
  endTime: '18:00',
}))

interface Props {
  masterId: string
  companyId: string
  onClose: () => void
}

export function WeeklyTemplateModal({ masterId, companyId, onClose }: Props) {
  const qc = useQueryClient()
  const [days, setDays] = useState<DayTemplate[]>(DEFAULT_DAYS)

  const { data: templateData } = useQuery({
    queryKey: ['schedule-template', masterId, companyId],
    queryFn: () => scheduleTemplateApi.get(masterId, companyId),
  })

  useEffect(() => {
    if (templateData && templateData.length > 0) setDays(templateData)
  }, [templateData])

  const saveMut = useMutation({
    mutationFn: () => scheduleTemplateApi.save({ masterId, companyId, days }),
  })

  const applyMut = useMutation({
    mutationFn: ({ from, to }: { from: string; to: string }) =>
      scheduleTemplateApi.apply(masterId, companyId, from, to),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['working-hours'] })
      onClose()
    },
  })

  function updateDay(idx: number, patch: Partial<DayTemplate>) {
    setDays(prev => prev.map((d, i) => i === idx ? { ...d, ...patch } : d))
  }

  function handleApplyMonth() {
    const now = new Date()
    applyMut.mutate({
      from: format(startOfMonth(now), 'yyyy-MM-dd'),
      to: format(endOfMonth(now), 'yyyy-MM-dd'),
    })
  }

  function handleApply3Months() {
    const now = new Date()
    applyMut.mutate({
      from: format(now, 'yyyy-MM-dd'),
      to: format(addDays(now, 90), 'yyyy-MM-dd'),
    })
  }

  return (
    <Modal title="Шаблон расписания на неделю" onClose={onClose}>
      <div className="flex flex-col gap-3">
        {days.map((day, idx) => (
          <div key={day.dayOfWeek} className="flex items-center gap-3 flex-wrap">
            <span className="w-7 text-sm font-medium text-ink shrink-0">{DAY_NAMES[day.dayOfWeek]}</span>
            <label className="flex items-center gap-2 cursor-pointer shrink-0">
              <input
                type="checkbox"
                checked={day.isWorking}
                onChange={e => updateDay(idx, { isWorking: e.target.checked })}
                className="w-4 h-4 accent-gold"
              />
              <span className="text-sm text-ink-soft">Рабочий</span>
            </label>
            {day.isWorking && (
              <div className="flex items-center gap-2 ml-auto">
                <input
                  type="time"
                  value={day.startTime}
                  onChange={e => updateDay(idx, { startTime: e.target.value })}
                  className="rounded-xl border border-line px-2 py-1.5 text-sm outline-none focus:border-gold bg-white text-ink"
                />
                <span className="text-muted text-sm">—</span>
                <input
                  type="time"
                  value={day.endTime}
                  onChange={e => updateDay(idx, { endTime: e.target.value })}
                  className="rounded-xl border border-line px-2 py-1.5 text-sm outline-none focus:border-gold bg-white text-ink"
                />
              </div>
            )}
            {!day.isWorking && (
              <span className="ml-auto text-sm text-muted">Выходной</span>
            )}
          </div>
        ))}

        {(saveMut.isError || applyMut.isError) && (
          <p className="text-sm text-danger">Произошла ошибка. Попробуйте снова.</p>
        )}

        <div className="flex flex-col gap-2 pt-2 border-t border-line mt-1">
          <Button
            onClick={() => saveMut.mutate()}
            loading={saveMut.isPending}
            className="w-full"
          >
            Сохранить шаблон
          </Button>
          <div className="grid grid-cols-2 gap-2">
            <Button
              variant="secondary"
              onClick={handleApplyMonth}
              loading={applyMut.isPending}
            >
              Применить к месяцу
            </Button>
            <Button
              variant="secondary"
              onClick={handleApply3Months}
              loading={applyMut.isPending}
            >
              Применить к 3 мес.
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  )
}
