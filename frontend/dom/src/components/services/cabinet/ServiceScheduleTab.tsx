import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { fmtDateTime } from '@/utils/dateFormat'
import { staysServicesApi } from '../../../api/staysServices'
import type { ScheduleSaveResultDto, ServiceMonthDayDto, WeeklyDayDto, WindowInput } from '../../../types'
import { businessDateLabel } from '../../../utils/serviceTimeFormat'
import { validateWindows } from '../../../utils/serviceWindows'
import { addMonths, firstOfMonth, formatMonthTitle } from '../../../utils/stayDates'
import { businessDateOf } from '../../../utils/businessClock'
import { getStayErrorMessage } from '../../../utils/stayError'
import { SavedNote, SectionCard } from '../../cabinet/formParts'
import { ErrorState, Skeleton } from '../../StatePanels'
import { useServiceTab } from './serviceContext'
import { WindowsEditor } from './WindowsEditor'

const MIDNIGHT_HINT =
  'День услуги идёт с 06:00 до 06:00 следующего дня. Время после полуночи — это та же ночь: окно «18:00 – 02:00 (след. дня)» начинается вечером выбранного дня и заканчивается ночью после него.'

/** What the save changed for sessions already booked: they stay in force, the owner is told how many and which (§39.28). */
function OutsideSessions({ result, companyId }: { result: ScheduleSaveResultDto | null; companyId: string }) {
  if (!result || result.outsideSessions.length === 0) return null
  return (
    <div role="status" className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning" data-testid="outside-sessions">
      <p className="font-semibold">{result.warningText ?? 'Есть сеансы вне нового расписания — они остаются в силе'}</p>
      <ul className="mt-1.5 flex flex-col gap-1">
        {result.outsideSessions.map((s) => (
          <li key={s.sessionId}>
            <Link to={`/cabinet/${companyId}/service-sessions/${s.sessionId}`} className="font-medium underline">
              {s.label}
            </Link>
            {s.houseName ? ` · ${s.houseName}` : ' · без проживания'}
          </li>
        ))}
      </ul>
    </div>
  )
}

/** «Расписание»: the weekly template (`ManageServices`) and the manual dates of the calendar (`ManageServiceDates`). */
export function ServiceScheduleTab() {
  const { canManage, canManageDates } = useServiceTab()
  return (
    <div className="flex flex-col gap-6">
      <p className="rounded-2xl bg-cream-deep px-4 py-3 text-sm text-ink-soft">{MIDNIGHT_HINT}</p>
      {canManage ? <WeeklyCard /> : <p className="text-sm text-ink-soft">Недельный шаблон меняет владелец.</p>}
      {canManageDates ? <DatesCard /> : <p className="text-sm text-ink-soft">Ручные даты меняет сотрудник с правом на даты услуг.</p>}
    </div>
  )
}

function WeeklyCard() {
  const { companyId, service } = useServiceTab()
  const qc = useQueryClient()
  const key = ['stays-service-weekly', companyId, service.id]
  const query = useQuery({ queryKey: key, queryFn: () => staysServicesApi.weekly(companyId, service.id), staleTime: 0 })
  if (query.isLoading) return <Skeleton className="h-64" />
  if (query.isError || !query.data) return <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить расписание.')} onRetry={() => void query.refetch()} />
  return <WeeklyEditor key={JSON.stringify(query.data)} initial={query.data.days} onSaved={(w) => qc.setQueryData(key, w)} />
}

function WeeklyEditor({ initial, onSaved }: { initial: WeeklyDayDto[]; onSaved: (w: { days: WeeklyDayDto[] }) => void }) {
  const { companyId, service } = useServiceTab()
  const [days, setDays] = useState<{ dayOfWeek: number; label: string; windows: WindowInput[] }[]>(() =>
    initial.map((d) => ({ dayOfWeek: d.dayOfWeek, label: d.label, windows: d.windows.map((w) => ({ startMinute: w.startMinute, endMinute: w.endMinute })) })),
  )
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<ScheduleSaveResultDto | null>(null)

  const invalid = useMemo(() => days.some((d) => !validateWindows(d.windows).ok), [days])
  const set = (dayOfWeek: number, windows: WindowInput[]) => {
    setDays((list) => list.map((d) => (d.dayOfWeek === dayOfWeek ? { ...d, windows } : d)))
    setSaved(false)
  }
  const copyFrom = (dayOfWeek: number, target: 'weekdays' | 'all') => {
    const src = days.find((d) => d.dayOfWeek === dayOfWeek)!.windows
    setDays((list) => list.map((d) => ((target === 'all' || d.dayOfWeek <= 5) && d.dayOfWeek !== dayOfWeek ? { ...d, windows: src.map((w) => ({ ...w })) } : d)))
    setSaved(false)
  }

  const save = useMutation({
    mutationFn: () => staysServicesApi.saveWeekly(companyId, service.id, { days: days.map((d) => ({ dayOfWeek: d.dayOfWeek, windows: d.windows })) }),
    onSuccess: (r) => {
      setResult(r)
      setSaved(true)
      setError('')
      if (r.weekly) onSaved(r.weekly)
    },
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось сохранить расписание.')),
  })

  return (
    <SectionCard title="Недельный шаблон" description="Окна, в которые услугу можно бронировать, по дням недели. Не больше трёх окон в день; день без окон — закрыт.">
      <form
        noValidate
        className="flex flex-col gap-5"
        onSubmit={(e) => {
          e.preventDefault()
          if (!invalid) save.mutate()
        }}
      >
        {days.map((d) => (
          <fieldset key={d.dayOfWeek} className="rounded-2xl border border-line p-4">
            <legend className="px-1 text-sm font-semibold text-ink">{d.label}</legend>
            <WindowsEditor idPrefix={`w-${d.dayOfWeek}`} windows={d.windows} onChange={(w) => set(d.dayOfWeek, w)} />
            <div className="mt-2 flex flex-wrap gap-2">
              <Button type="button" variant="ghost" size="sm" className="min-h-[44px]" onClick={() => copyFrom(d.dayOfWeek, 'weekdays')}>
                Скопировать на будни
              </Button>
              <Button type="button" variant="ghost" size="sm" className="min-h-[44px]" onClick={() => copyFrom(d.dayOfWeek, 'all')}>
                Скопировать на все дни
              </Button>
            </div>
          </fieldset>
        ))}
        {error && <InlineError>{error}</InlineError>}
        <OutsideSessions result={result} companyId={companyId} />
        <div className="flex items-center gap-3">
          <Button type="submit" size="lg" loading={save.isPending} disabled={invalid} className="min-h-[44px]">
            Сохранить шаблон
          </Button>
          <SavedNote show={saved} />
        </div>
      </form>
    </SectionCard>
  )
}

function DatesCard() {
  const { companyId, service } = useServiceTab()
  const [month, setMonth] = useState(() => firstOfMonth(businessDateOf(new Date()).businessDate))
  const [selected, setSelected] = useState<string | null>(null)
  const key = ['stays-service-month', companyId, service.id, month]
  const query = useQuery({ queryKey: key, queryFn: () => staysServicesApi.month(companyId, service.id, month.slice(0, 7)), staleTime: 0 })
  const day = query.data?.days.find((d) => d.businessDate === selected) ?? null

  return (
    <SectionCard title="Ручные даты" description="Особый график на конкретный день: праздник, закрытие на ремонт, дополнительные окна. Прошедшие даты менять нельзя.">
      <div className="flex items-center justify-between gap-3">
        <Button type="button" variant="secondary" size="sm" className="min-h-[44px]" onClick={() => setMonth(addMonths(month, -1))}>
          ← Раньше
        </Button>
        <h3 className="text-[15px] font-semibold text-ink">{formatMonthTitle(month)}</h3>
        <Button type="button" variant="secondary" size="sm" className="min-h-[44px]" onClick={() => setMonth(addMonths(month, 1))}>
          Позже →
        </Button>
      </div>
      {query.isLoading ? (
        <Skeleton className="h-64" />
      ) : query.isError || !query.data ? (
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить даты.')} onRetry={() => void query.refetch()} />
      ) : (
        <ul className="grid gap-2 sm:grid-cols-2" aria-label="Дни месяца">
          {query.data.days.map((d) => {
            const past = d.businessDate < query.data!.today
            return (
              <li key={d.businessDate}>
                <button
                  type="button"
                  aria-pressed={selected === d.businessDate}
                  onClick={() => setSelected(d.businessDate)}
                  className={`flex min-h-[56px] w-full flex-col items-start rounded-2xl border px-3 py-2 text-left text-sm ${
                    selected === d.businessDate ? 'border-ink bg-cream-deep' : 'border-line bg-white hover:border-line-strong'
                  } ${past ? 'opacity-60' : ''}`}
                >
                  <span className="font-semibold text-ink">
                    {businessDateLabel(d.businessDate)} <SourceBadge source={d.source} />
                  </span>
                  <span className="text-xs text-ink-soft">{d.source === 'Closed' || d.windows.length === 0 ? 'закрыто' : d.windows.map((w) => w.label).join('; ')}</span>
                </button>
              </li>
            )
          })}
        </ul>
      )}
      {day && query.data && <DayEditor key={day.businessDate + (day.updatedAtUtc ?? '')} day={day} today={query.data.today} onSaved={() => void query.refetch()} />}
    </SectionCard>
  )
}

function SourceBadge({ source }: { source: ServiceMonthDayDto['source'] }) {
  const text = source === 'Template' ? 'по шаблону' : source === 'Override' ? 'вручную' : 'закрыто вручную'
  return <span className="ml-1 rounded-full bg-cream-deep px-2 py-0.5 text-[11px] font-medium text-ink-soft">{text}</span>
}

function DayEditor({ day, today, onSaved }: { day: ServiceMonthDayDto; today: string; onSaved: () => void }) {
  const { companyId, service } = useServiceTab()
  const past = day.businessDate < today
  const [closed, setClosed] = useState(day.source === 'Closed')
  const [windows, setWindows] = useState<WindowInput[]>(() => day.windows.map((w) => ({ startMinute: w.startMinute, endMinute: w.endMinute })))
  const [comment, setComment] = useState(day.comment ?? '')
  const [error, setError] = useState('')
  const [result, setResult] = useState<ScheduleSaveResultDto | null>(null)
  const [saved, setSaved] = useState(false)

  const invalid = !closed && !validateWindows(windows).ok
  const done = (r: ScheduleSaveResultDto) => {
    setResult(r)
    setSaved(true)
    setError('')
    onSaved()
  }
  const save = useMutation({
    mutationFn: () => staysServicesApi.saveDate(companyId, service.id, day.businessDate, { closed, windows: closed ? [] : windows, comment: comment.trim() || null }),
    onSuccess: done,
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось сохранить день.')),
  })
  const reset = useMutation({
    mutationFn: () => staysServicesApi.resetDate(companyId, service.id, day.businessDate),
    onSuccess: done,
    onError: (err) => setError(getStayErrorMessage(err, 'Не удалось вернуть шаблон.')),
  })

  return (
    <form
      noValidate
      className="mt-2 flex flex-col gap-4 rounded-2xl border border-line-strong bg-cream/40 p-4"
      aria-label={`Настройка дня ${businessDateLabel(day.businessDate)}`}
      onSubmit={(e) => {
        e.preventDefault()
        if (!invalid && !past) save.mutate()
      }}
    >
      <h4 className="text-[15px] font-semibold text-ink">{businessDateLabel(day.businessDate)}</h4>
      {past && <p className="text-sm text-ink-soft">Нельзя менять прошедшие даты</p>}
      <label className="flex min-h-[44px] cursor-pointer items-center gap-3 text-sm text-ink">
        <input type="checkbox" checked={closed} disabled={past} onChange={(e) => setClosed(e.target.checked)} className="h-5 w-5 accent-gold" />
        Закрыто в этот день
      </label>
      {!closed && <WindowsEditor idPrefix={`d-${day.businessDate}`} windows={windows} onChange={setWindows} disabled={past} />}
      <div className="flex flex-col gap-1.5">
        <label htmlFor="override-comment" className="text-[13px] font-medium text-[#4A4038]">
          Комментарий <span className="font-normal text-muted">(только для сотрудников, до 300 символов)</span>
        </label>
        <textarea
          id="override-comment"
          rows={2}
          maxLength={300}
          disabled={past}
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          className="rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
        />
      </div>
      {day.updatedByName && day.updatedAtUtc && (
        <p className="text-xs text-muted">
          Изменил(а) {day.updatedByName}, {fmtDateTime(day.updatedAtUtc)}
        </p>
      )}
      {error && <InlineError>{error}</InlineError>}
      <OutsideSessions result={result} companyId={companyId} />
      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" loading={save.isPending} disabled={invalid || past} className="min-h-[44px]">
          Сохранить день
        </Button>
        {day.source !== 'Template' && (
          <Button type="button" variant="secondary" loading={reset.isPending} disabled={past} className="min-h-[44px]" onClick={() => reset.mutate()}>
            Вернуть по шаблону
          </Button>
        )}
        <SavedNote show={saved} />
      </div>
    </form>
  )
}
