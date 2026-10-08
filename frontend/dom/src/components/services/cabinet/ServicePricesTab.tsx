import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { formatRub } from '@/utils/money'
import { staysServicesApi } from '../../../api/staysServices'
import type { PriceRuleDto, PriceRulesDto, StaysServiceConflictDto } from '../../../types'
import { clockOf, MINUTES_PER_DAY } from '../../../utils/businessClock'
import { PRICE_RULE_ERROR_TEXT, validatePriceRule } from '../../../utils/serviceWindows'
import { getStayErrorMessage, readConflict } from '../../../utils/stayError'
import { ConfirmDialog } from '../ConfirmDialog'
import { SectionCard } from '../../cabinet/formParts'
import { ErrorState, Skeleton } from '../../StatePanels'
import { useServiceTab } from './serviceContext'

const DAYS = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']
const FROM_HOURS = Array.from({ length: 24 }, (_, i) => 6 + i) // 6 … 29
const TO_HOURS = Array.from({ length: 24 }, (_, i) => 7 + i) // 7 … 30
const hourLabel = (h: number) => `${clockOf(h * 60)}${h * 60 >= MINUTES_PER_DAY ? ' (след. дня)' : ''}`

interface Draft {
  id: string | null
  daysMask: number
  fromHour: number
  toHour: number
  priceRub: string
}
const EMPTY: Draft = { id: null, daysMask: 0, fromHour: 10, toHour: 18, priceRub: '' }

/** «Цены»: price rules (a rule = days of the week × hours of the business day × price per hour) and the 7 × 24 preview. */
export function ServicePricesTab() {
  const { companyId, service, canManage } = useServiceTab()
  const qc = useQueryClient()
  const key = ['stays-service-price-rules', companyId, service.id]
  const query = useQuery({ queryKey: key, queryFn: () => staysServicesApi.priceRules(companyId, service.id), staleTime: 0 })
  const [draft, setDraft] = useState<Draft | null>(null)
  const [error, setError] = useState('')
  const [conflict, setConflict] = useState<StaysServiceConflictDto | null>(null)
  const [deleting, setDeleting] = useState<PriceRuleDto | null>(null)

  const apply = (r: PriceRulesDto) => {
    qc.setQueryData(key, r)
    void qc.invalidateQueries({ queryKey: ['stays-service', companyId, service.id] }) // publish problems depend on the rules
    setDraft(null)
    setError('')
    setConflict(null)
  }
  const fail = (err: unknown) => {
    const c = readConflict<StaysServiceConflictDto>(err)
    setConflict(c?.code === 'PriceRuleOverlap' ? c : null)
    setError(getStayErrorMessage(err, 'Не удалось сохранить правило.'))
  }
  const save = useMutation({
    mutationFn: (d: Draft) => {
      const body = { daysMask: d.daysMask, fromHour: d.fromHour, toHour: d.toHour, priceRub: Number(d.priceRub) }
      return d.id ? staysServicesApi.updatePriceRule(companyId, service.id, d.id, body) : staysServicesApi.addPriceRule(companyId, service.id, body)
    },
    onSuccess: apply,
    onError: fail,
  })
  const remove = useMutation({
    mutationFn: (id: string) => staysServicesApi.deletePriceRule(companyId, service.id, id),
    onSuccess: (r) => {
      apply(r)
      setDeleting(null)
    },
    onError: (err) => {
      setDeleting(null)
      fail(err)
    },
  })

  if (query.isLoading) return <Skeleton className="h-64" />
  if (query.isError || !query.data) return <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить цены.')} onRetry={() => void query.refetch()} />
  const data = query.data

  const localProblem = (() => {
    if (!draft) return null
    const existing = data.rules.filter((r) => r.id !== draft.id)
    const r = validatePriceRule({ daysMask: draft.daysMask, fromHour: draft.fromHour, toHour: draft.toHour, priceRub: Number(draft.priceRub) }, existing)
    return r.ok || r.error === 'PriceRuleOverlap' ? null : PRICE_RULE_ERROR_TEXT[r.error]
  })()

  return (
    <div className="flex flex-col gap-6">
      <SectionCard
        title="Правила цены"
        description="Цена за час зависит от дня недели и времени начала часа. Час, который начинается после полуночи, считается по правилам дня, в который начался сеанс. Правила не должны пересекаться."
      >
        {data.rules.length === 0 ? (
          <p className="rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning">Правил пока нет — без цены услугу нельзя опубликовать.</p>
        ) : (
          <ul className="flex flex-col gap-2" aria-label="Правила цены">
            {data.rules.map((r) => (
              <li key={r.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-line px-4 py-3">
                <p className="text-sm text-ink">
                  {r.label} — <span className="font-semibold tabular-nums">{formatRub(r.priceRub)}/ч</span>
                </p>
                {canManage && (
                  <div className="flex gap-2">
                    <Button type="button" variant="secondary" size="sm" className="min-h-[44px]" onClick={() => setDraft({ id: r.id, daysMask: r.daysMask, fromHour: r.fromHour, toHour: r.toHour, priceRub: String(r.priceRub) })}>
                      Изменить
                    </Button>
                    <Button type="button" variant="danger" size="sm" className="min-h-[44px]" onClick={() => setDeleting(r)}>
                      Удалить
                    </Button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}

        {canManage && !draft && (
          <Button type="button" variant="secondary" className="min-h-[44px] self-start" onClick={() => setDraft({ ...EMPTY })}>
            Добавить правило
          </Button>
        )}

        {canManage && draft && (
          <form
            noValidate
            className="flex flex-col gap-4 rounded-2xl border border-line-strong bg-cream/40 p-4"
            aria-label={draft.id ? 'Изменить правило цены' : 'Новое правило цены'}
            onSubmit={(e) => {
              e.preventDefault()
              if (!localProblem) save.mutate(draft)
            }}
          >
            <fieldset>
              <legend className="mb-1.5 text-[13px] font-medium text-[#4A4038]">Дни недели</legend>
              <div className="flex flex-wrap gap-2">
                {DAYS.map((d, i) => (
                  <label key={d} className="flex min-h-[44px] min-w-[52px] cursor-pointer items-center justify-center gap-1.5 rounded-xl border border-line bg-white px-3 text-sm has-[:checked]:border-ink has-[:checked]:bg-ink has-[:checked]:text-cream">
                    <input type="checkbox" className="sr-only" checked={(draft.daysMask & (1 << i)) !== 0} onChange={() => setDraft({ ...draft, daysMask: draft.daysMask ^ (1 << i) })} />
                    {d}
                  </label>
                ))}
              </div>
            </fieldset>
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="flex flex-col gap-1.5">
                <label htmlFor="rule-from" className="text-[13px] font-medium text-[#4A4038]">С</label>
                <select id="rule-from" value={draft.fromHour} onChange={(e) => setDraft({ ...draft, fromHour: Number(e.target.value) })} className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm">
                  {FROM_HOURS.map((h) => <option key={h} value={h}>{hourLabel(h)}</option>)}
                </select>
              </div>
              <div className="flex flex-col gap-1.5">
                <label htmlFor="rule-to" className="text-[13px] font-medium text-[#4A4038]">До</label>
                <select id="rule-to" value={draft.toHour} onChange={(e) => setDraft({ ...draft, toHour: Number(e.target.value) })} className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm">
                  {TO_HOURS.map((h) => <option key={h} value={h}>{hourLabel(h)}</option>)}
                </select>
              </div>
              <div className="flex flex-col gap-1.5">
                <label htmlFor="rule-price" className="text-[13px] font-medium text-[#4A4038]">Цена за час, ₽</label>
                <input id="rule-price" type="number" inputMode="numeric" min={1} max={100000} value={draft.priceRub} onChange={(e) => setDraft({ ...draft, priceRub: e.target.value })} className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm" />
              </div>
            </div>
            {(localProblem || error) && (
              <p role="alert" className="text-sm text-danger" data-testid="rule-error">
                {localProblem ?? error}
              </p>
            )}
            {conflict?.conflictingRule && <p className="text-xs text-ink-soft">Мешает правило: {conflict.conflictingRule.label} — {formatRub(conflict.conflictingRule.priceRub)}/ч</p>}
            <div className="flex gap-3">
              <Button type="submit" loading={save.isPending} disabled={!!localProblem} className="min-h-[44px]">
                Сохранить правило
              </Button>
              <Button type="button" variant="secondary" className="min-h-[44px]" onClick={() => { setDraft(null); setError(''); setConflict(null) }}>
                Отмена
              </Button>
            </div>
          </form>
        )}
        {!draft && error && <InlineError>{error}</InlineError>}
      </SectionCard>

      <SectionCard title="Предпросмотр: день × час" description="Цена каждого часа по правилам. «нет цены» в рабочее время — час, с которого сеанс начать нельзя.">
        <div className="overflow-x-auto">
          <table className="min-w-full border-collapse text-xs" data-testid="price-matrix">
            <caption className="sr-only">Цена за час по дням недели и часам бизнес-дня</caption>
            <thead>
              <tr>
                <th scope="col" className="sticky left-0 bg-white px-2 py-1.5 text-left font-medium text-ink-soft">День</th>
                {data.matrix[0]?.cells.map((c) => (
                  <th key={c.hour} scope="col" className="whitespace-nowrap px-1.5 py-1.5 text-left font-medium text-ink-soft">{c.label}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.matrix.map((row) => (
                <tr key={row.dayOfWeek}>
                  <th scope="row" className="sticky left-0 bg-white px-2 py-1.5 text-left font-medium text-ink">{row.label}</th>
                  {row.cells.map((c) => (
                    <td key={c.hour} className={`whitespace-nowrap border border-line/60 px-1.5 py-1.5 text-center tabular-nums ${c.inWindowWithoutPrice ? 'bg-warning-bg text-warning' : 'text-ink'}`}>
                      {c.priceRub != null ? c.priceRub : c.inWindowWithoutPrice ? 'нет цены' : '—'}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </SectionCard>

      {deleting && (
        <ConfirmDialog
          title="Удалить правило цены?"
          text={`${deleting.label} — ${formatRub(deleting.priceRub)}/ч. Часы без правила нельзя будет выбрать для сеанса.`}
          confirmLabel="Удалить правило"
          pending={remove.isPending}
          onConfirm={() => remove.mutate(deleting.id)}
          onClose={() => setDeleting(null)}
        />
      )}
    </div>
  )
}
