import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { formatPhone, telHref } from '@/utils/phone'
import { scheduleApi } from '../../api/schedule'
import { ErrorState, InlineError, LoadingList } from '../StatePanels'
import { intervalFieldError, type EditableInterval } from '../../utils/hours'
import { getGoodsErrorMessage, readConflict } from '../../utils/orderError'
import type { CatalogConflictDto, SpecialDayInput } from '../../types'

const FIELD = 'rounded-xl border border-line bg-white px-3 py-2 text-sm text-ink outline-none focus:border-gold min-h-[44px]'

/**
 * US-24-02 (P1) — special days: a closed day or its own hours. When active orders would fall outside the new hours the
 * server answers 409 `ScheduleConflictsWithOrders` with the list; the owner sees it and may confirm (orders are NOT changed).
 */
export function SpecialDays({ shopId, onChanged }: { shopId: string; onChanged: () => void }) {
  const qc = useQueryClient()
  const key = ['special-days', shopId]
  const list = useQuery({ queryKey: key, queryFn: () => scheduleApi.specialDays(shopId) })
  const [date, setDate] = useState('')
  const [closed, setClosed] = useState(true)
  const [intervals, setIntervals] = useState<EditableInterval[]>([{ start: '10:00', end: '16:00' }])
  const [localError, setLocalError] = useState<string | null>(null)
  const [conflict, setConflict] = useState<{ body: SpecialDayInput; date: string; info: CatalogConflictDto } | null>(null)

  const put = useMutation({
    mutationFn: (v: { date: string; body: SpecialDayInput }) => scheduleApi.putSpecialDay(shopId, v.date, v.body),
    onSuccess: () => {
      setConflict(null)
      setDate('')
      void qc.invalidateQueries({ queryKey: key })
      onChanged()
    },
    onError: (err, v) => {
      const c = readConflict<CatalogConflictDto>(err)
      if (c?.code === 'ScheduleConflictsWithOrders') setConflict({ body: v.body, date: v.date, info: c })
    },
  })
  const remove = useMutation({
    mutationFn: (d: string) => scheduleApi.deleteSpecialDay(shopId, d),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: key })
      onChanged()
    },
  })

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    setLocalError(null)
    if (!date) return setLocalError('Укажите дату')
    if (!closed) {
      if (intervals.length === 0) return setLocalError('Задайте часы или отметьте день как выходной')
      const problem = intervals.map(intervalFieldError).find(Boolean)
      if (problem) return setLocalError(problem)
    }
    put.mutate({ date, body: closed ? { isClosed: true, confirmConflicts: false } : { isClosed: false, intervals: intervals.map((i) => ({ start: i.start, end: i.end })), confirmConflicts: false } })
  }

  return (
    <section aria-labelledby="special-days-title" className="rounded-2xl border border-line bg-white p-5 sm:p-6">
      <h2 id="special-days-title" className="font-serif text-xl text-ink">
        Особые дни
      </h2>
      <p className="text-sm text-ink-soft mt-1">Праздники и нестандартные часы. Дата — от сегодня до 90 дней вперёд.</p>

      <div className="mt-4">
        {list.isLoading ? (
          <LoadingList rows={2} rowClass="h-12" />
        ) : list.isError ? (
          <ErrorState message={getGoodsErrorMessage(list.error, 'Не удалось загрузить особые дни.')} onRetry={() => void list.refetch()} />
        ) : !list.data || list.data.length === 0 ? (
          <p className="text-sm text-muted">Особых дней нет — действует недельное расписание.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-line">
            {list.data.map((d) => (
              <li key={d.date} className="py-2.5 flex items-center justify-between gap-3">
                <p className="text-sm text-ink">
                  <span className="font-semibold">{d.label}</span> · {d.text}
                </p>
                <Button variant="ghost" size="sm" className="min-h-[44px]" loading={remove.isPending && remove.variables === d.date} onClick={() => remove.mutate(d.date)} aria-label={`Вернуть обычные часы: ${d.label}`}>
                  Убрать
                </Button>
              </li>
            ))}
          </ul>
        )}
        {remove.isError && (
          <div className="mt-2">
            <InlineError>{getGoodsErrorMessage(remove.error, 'Не удалось убрать особый день.')}</InlineError>
          </div>
        )}
      </div>

      <form onSubmit={submit} noValidate className="mt-5 pt-5 border-t border-line flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="special-date" className="text-[13px] font-medium text-[#4A4038]">
            Дата
          </label>
          <input id="special-date" type="date" className={`${FIELD} w-fit`} value={date} onChange={(e) => setDate(e.target.value)} />
        </div>
        <label className="flex items-center gap-3 text-sm text-ink cursor-pointer min-h-[44px]">
          <input type="checkbox" className="h-5 w-5 accent-[#2B2420]" checked={closed} onChange={(e) => setClosed(e.target.checked)} />
          Выходной — магазин не работает
        </label>
        {!closed && (
          <div className="flex flex-col gap-2">
            {intervals.map((i, idx) => (
              <div key={idx} className="flex items-center gap-2 flex-wrap">
                <label className="sr-only" htmlFor={`sp-s-${idx}`}>{`Интервал ${idx + 1}: с`}</label>
                <input id={`sp-s-${idx}`} type="time" step={300} className={FIELD} value={i.start} onChange={(e) => setIntervals(intervals.map((x, k) => (k === idx ? { ...x, start: e.target.value } : x)))} />
                <span aria-hidden="true">–</span>
                <label className="sr-only" htmlFor={`sp-e-${idx}`}>{`Интервал ${idx + 1}: до`}</label>
                <input id={`sp-e-${idx}`} type="time" step={300} className={FIELD} value={i.end} onChange={(e) => setIntervals(intervals.map((x, k) => (k === idx ? { ...x, end: e.target.value } : x)))} />
                {intervals.length > 1 && (
                  <Button variant="ghost" size="sm" className="min-h-[44px]" onClick={() => setIntervals(intervals.filter((_, k) => k !== idx))}>
                    Убрать
                  </Button>
                )}
              </div>
            ))}
            {intervals.length < 3 && (
              <Button variant="ghost" size="sm" className="self-start min-h-[44px]" onClick={() => setIntervals([...intervals, { start: '', end: '' }])}>
                Ещё интервал
              </Button>
            )}
          </div>
        )}
        {(localError || (put.isError && !conflict)) && <InlineError>{localError ?? getGoodsErrorMessage(put.error, 'Не удалось сохранить особый день.')}</InlineError>}
        <Button type="submit" className="self-start" loading={put.isPending && !conflict}>
          Сохранить день
        </Button>
      </form>

      {conflict && (
        <Modal title="На этот день уже есть заказы" onClose={() => setConflict(null)} dismissible={!put.isPending}>
          <p className="text-sm text-ink-soft" data-testid="conflict-message">
            {conflict.info.message}
          </p>
          <ul className="mt-3 flex flex-col gap-2 max-h-64 overflow-y-auto" aria-label="Заказы вне новых часов">
            {(conflict.info.conflictingOrders ?? []).map((o) => (
              <li key={o.orderId} className="rounded-xl border border-line bg-white px-3 py-2 text-sm">
                <p className="font-semibold text-ink">
                  № {o.number} · {o.pickupText} · <span className="font-normal text-ink-soft">{o.statusText}</span>
                </p>
                <p className="text-xs text-ink-soft">
                  {o.customerName ?? 'Имя удалено'}
                  {o.customerPhone && (
                    <>
                      {' · '}
                      <a href={telHref(o.customerPhone) || undefined} className="text-gold hover:text-gold-dark inline-flex items-center min-h-[44px]">
                        {formatPhone(o.customerPhone)}
                      </a>
                    </>
                  )}
                </p>
              </li>
            ))}
          </ul>
          <p className="text-xs text-muted mt-3">Заказы не изменятся — свяжитесь с покупателями или отмените их на экране заказов.</p>
          <div className="flex gap-3 mt-5">
            <Button variant="secondary" className="flex-1" onClick={() => setConflict(null)} disabled={put.isPending}>
              Отмена
            </Button>
            <Button className="flex-1" loading={put.isPending} onClick={() => put.mutate({ date: conflict.date, body: { ...conflict.body, confirmConflicts: true } })}>
              Всё равно сохранить
            </Button>
          </div>
        </Modal>
      )}
    </section>
  )
}
